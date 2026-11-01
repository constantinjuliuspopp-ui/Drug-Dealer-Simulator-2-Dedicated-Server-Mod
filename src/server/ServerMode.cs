using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using DDS2Mod.Shared;

namespace DDS2Mod.Server;

/// <summary>
/// DDS2Mod.exe --server: startet eine Spielinstanz als Host ("Pseudo-Dedicated"),
/// zeigt ein Konsolen-Log mit Zeitstempel und nimmt Befehle entgegen.
/// </summary>
public static class ServerMode
{
    [DllImport("kernel32.dll")] static extern bool AllocConsole();
    [DllImport("kernel32.dll")] static extern bool FreeConsole();

    static StreamWriter? _logFile;
    static readonly object LogLock = new();

    public static int Run(string[] args)
    {
        // WinExe hat keine Konsole: eigene anlegen, damit Eingaben zuverlässig ankommen.
        FreeConsole();
        AllocConsole();
        Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true });
        Console.SetIn(new StreamReader(Console.OpenStandardInput(), Console.InputEncoding));
        Console.OutputEncoding = Encoding.UTF8;
        Console.Title = $"DDS2 Server {ModInfo.Version}";

        try
        {
            return RunServer(args);
        }
        catch (Exception ex)
        {
            Log("FEHLER", ex.Message);
            Console.WriteLine("Enter drücken zum Schließen.");
            Console.ReadLine();
            return 1;
        }
        finally
        {
            _logFile?.Dispose();
        }
    }

    static int RunServer(string[] args)
    {
        var cfgPath = Path.GetFullPath(ServerConfig.ConfigPathFromArgs(args) ?? Path.Combine(AppContext.BaseDirectory, "server.cfg"));
        var serverDir = Path.GetDirectoryName(cfgPath)!;
        Directory.CreateDirectory(Path.Combine(serverDir, "logs"));
        _logFile = new StreamWriter(Path.Combine(serverDir, "logs", $"server-{DateTime.Now:yyyyMMdd-HHmmss}.log"), append: false, new UTF8Encoding(false)) { AutoFlush = true };

        var cfg = ServerConfig.Load(cfgPath);
        cfg.ApplyArgs(args);
        Log("INFO", $"DDS2 Server {ModInfo.Version} – Konfiguration: {cfgPath}");
        Log("INFO", $"Name '{cfg.ServerName}', Port {cfg.Port}/UDP, max. {cfg.MaxPlayers} Spieler, Welt '{cfg.World}', Passwort {(cfg.Password.Length > 0 ? "an" : "aus")}");
        if (cfg.MaxPlayers > 3)
            Log("WARN", "Das Spiel ist für 3 Spieler ausgelegt – mehr ist experimentell.");

        var gameDir = string.IsNullOrEmpty(cfg.GameDir) ? GameLocator.FindGameDir() : cfg.GameDir;
        if (gameDir is null || !GameLocator.IsGameDir(gameDir))
            throw new InvalidOperationException("Spielordner nicht gefunden. In server.cfg 'GameDir=' setzen.");
        if (!GameProcess.IsModInstalled(gameDir))
            throw new InvalidOperationException("Die Mod ist im Spielordner nicht installiert. Bitte den Installer ausführen.");

        var backup = SaveBackup.Create();
        if (backup != null) Log("INFO", $"Spielstände gesichert: {backup}");
        IniOverride.Apply(new OverrideSettings(cfg.Port, cfg.MaxPlayers, cfg.Password));
        Log("INFO", "Netzwerk-Einstellungen gesetzt (Direkt-IP, Port, Spielerlimit, Versionscheck).");

        var ipc = new IpcChannel(Path.Combine(serverDir, "ipc"));
        ipc.Reset();

        var gameArgs = new List<string> { "-windowed", "-ResX=1280", "-ResY=720" };
        if (cfg.Headless)
        {
            gameArgs.AddRange(new[] { "-nullrhi", "-nosound" });
            Log("WARN", "Headless-Modus ist experimentell: die Welt muss automatisch laden, ohne Fenster kannst du nicht eingreifen.");
        }
        var saves = GameProcess.SaveFileNames();
        Log("INFO", saves.Count > 0 ? $"Spielstand-Dateien: {string.Join(", ", saves)}" : "Keine Spielstand-Dateien gefunden – zuerst im Spiel eine Welt anlegen.");

        var game = GameProcess.Launch(gameDir, "server", ipc.Dir, gameArgs,
            new Dictionary<string, string> { ["DDS2_WORLD"] = cfg.World });
        Log("INFO", $"Spiel gestartet (PID {game.Id}). Die Welt '{cfg.World}' wird automatisch geladen, sobald das Hauptmenü erscheint.");
        Log("INFO", "Klappt das nicht: Spielstand im Spielfenster von Hand laden (Koop) oder 'load <Name>' eingeben.");
        Log("INFO", "Befehle: status, kick <Name>, save, start, load <Welt>, stop, help");

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; ipc.Send("stop"); Log("INFO", "Strg+C – stoppe Server ..."); };

        var pump = Task.Run(() => PumpEvents(ipc, game, cfg, cts.Token));
        var input = new Thread(() => ReadCommands(ipc, game)) { IsBackground = true };
        input.Start();

        game.WaitForExit();
        cts.Cancel();
        pump.Wait();
        foreach (var e in ipc.ReadNewEvents()) Log(e.Level, e.Text);

        if (!GameLocator.IsGameRunning()) IniOverride.RestoreAll();
        Log("INFO", $"Spiel beendet (Exit-Code {game.ExitCode}). Server gestoppt.");
        return 0;
    }

    // Zeitpunkt der letzten save-Anforderung (UTC-Ticks, 0 = keine offen); von zwei Threads benutzt.
    static long _saveRequestedTicks;

    static void RequestSave(IpcChannel ipc)
    {
        ipc.Send("save");
        Interlocked.Exchange(ref _saveRequestedTicks, DateTime.UtcNow.Ticks);
    }

    // Prüft anhand der Dateizeiten, ob das Spiel nach "save" wirklich geschrieben hat.
    static void CheckSaveWritten()
    {
        var ticks = Interlocked.Read(ref _saveRequestedTicks);
        if (ticks == 0) return;
        var requested = new DateTime(ticks, DateTimeKind.Utc);
        var newest = GameProcess.NewestSaveFile();
        if (newest is { } n && n.Time > requested.AddSeconds(-1))
        {
            Log("INFO", $"Spielstand geschrieben ({n.File}).");
            Interlocked.CompareExchange(ref _saveRequestedTicks, 0, ticks);
        }
        else if (DateTime.UtcNow - requested > TimeSpan.FromSeconds(45))
        {
            Log("WARN", "Nach 45 s keine neue Spielstand-Datei – das Spiel hat das Speichern evtl. abgelehnt (Mindestabstand zwischen Speicherungen?).");
            Interlocked.CompareExchange(ref _saveRequestedTicks, 0, ticks);
        }
    }

    static async Task PumpEvents(IpcChannel ipc, Process game, ServerConfig cfg, CancellationToken ct)
    {
        var nextAutosave = cfg.AutosaveMinutes > 0 ? DateTime.Now.AddMinutes(cfg.AutosaveMinutes) : DateTime.MaxValue;
        var knownPlayers = new HashSet<string>();
        var lastState = "";
        var inWorld = false;
        while (!ct.IsCancellationRequested)
        {
            foreach (var e in ipc.ReadNewEvents())
                Log(e.Level, ErrorTexts.IsError(e) ? ErrorTexts.Describe(e) : e.Text);

            var status = ipc.ReadStatus();
            if (status != null && !status.IsStale)
            {
                var state = $"{status.Mode}/{status.Place}";
                if (state != lastState)
                {
                    Log(status.Mode == "client" ? "WARN" : "INFO", (status.Mode, status.Place) switch
                    {
                        ("client", _) => "Diese Instanz ist als Client verbunden, nicht als Host.",
                        (_, "menu") => "Spiel im Hauptmenü.",
                        ("host", "lobby") => $"Lobby läuft als Host – Spieler können sich jetzt auf Port {cfg.Port} verbinden.",
                        ("host", "world") => $"Welt läuft als Host – Spieler können sich auf Port {cfg.Port} verbinden.",
                        ("standalone", "world") => "Welt läuft im Einzelspieler-Modus – Mitspieler können NICHT beitreten. Spielstand als Koop laden.",
                        _ => $"Status: {status.Mode} / {status.Place}",
                    });
                    lastState = state;
                }
                inWorld = status.Mode == "host" && status.Place == "world";
                if (status.Mode == "host") TrackPlayers(status, knownPlayers);
            }

            if (DateTime.Now >= nextAutosave)
            {
                if (inWorld) { Log("INFO", "Autosave ..."); RequestSave(ipc); }
                nextAutosave = DateTime.Now.AddMinutes(cfg.AutosaveMinutes);
            }
            CheckSaveWritten();

            try { await Task.Delay(1000, ct); } catch (TaskCanceledException) { break; }
        }
    }

    static void TrackPlayers(ModStatus status, HashSet<string> known)
    {
        var current = status.Players.Select(p => p.Name).ToHashSet();
        foreach (var joined in current.Except(known)) Log("INFO", $"Spieler verbunden: {joined}");
        foreach (var left in known.Except(current)) Log("INFO", $"Spieler getrennt: {left}");
        known.Clear();
        known.UnionWith(current);
    }

    static void ReadCommands(IpcChannel ipc, Process game)
    {
        while (!game.HasExited)
        {
            var line = Console.ReadLine();
            if (line is null) return;
            var parts = line.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;
            switch (parts[0].ToLowerInvariant())
            {
                case "status": PrintStatus(ipc); break;
                case "kick":
                    if (parts.Length < 2) Log("WARN", "Benutzung: kick <Spielername>");
                    else ipc.Send($"kick {parts[1]}");
                    break;
                case "save": RequestSave(ipc); break;
                case "start": ipc.Send("start"); break;
                case "load":
                    if (parts.Length < 2) Log("WARN", "Benutzung: load <Name des Spielstands>");
                    else ipc.Send($"load {parts[1]}");
                    break;
                case "call" or "dump":
                    // Diagnose für die Weiterentwicklung (siehe docs/test-0.2.md)
                    ipc.Send(line.Trim());
                    break;
                case "stop":
                    Log("INFO", "Speichere und beende ...");
                    Interlocked.Exchange(ref _saveRequestedTicks, DateTime.UtcNow.Ticks);
                    ipc.Send("stop");
                    if (!game.WaitForExit(60_000))
                    {
                        Log("WARN", "Spiel reagiert nicht – wird beendet.");
                        game.Kill();
                    }
                    return;
                case "help":
                    Log("INFO", "status – Spieler und Zustand | kick <Name> – Spieler entfernen | save – speichern | "
                        + "start – Spiel aus der Lobby starten | load <Welt> – Welt laden (im Hauptmenü) | stop – speichern und beenden");
                    break;
                default: Log("WARN", $"Unbekannter Befehl '{parts[0]}' – 'help' für Hilfe"); break;
            }
        }
    }

    static void PrintStatus(IpcChannel ipc)
    {
        var s = ipc.ReadStatus();
        if (s is null || s.IsStale)
        {
            Log("INFO", "Keine Antwort der Mod (Spiel lädt noch oder UE4SS läuft nicht).");
            return;
        }
        if (s.Version != ModInfo.Version)
            Log("WARN", $"Mod im Spiel hat Version {s.Version}, Launcher {ModInfo.Version} – bitte neu installieren.");
        Log("INFO", $"Zustand: {s.Mode} / {s.Place}, Spieler: {s.Players.Count}");
        foreach (var (name, ping) in s.Players)
            Log("INFO", $"  {name}  ({(ping >= 0 ? ping + " ms" : "Ping ?")})");
    }

    static void Log(string level, string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}";
        lock (LogLock)
        {
            Console.ForegroundColor = level switch
            {
                "WARN" => ConsoleColor.Yellow,
                "FEHLER" or "ERROR" or "NETERROR" or "TRAVELERROR" => ConsoleColor.Red,
                _ => ConsoleColor.Gray,
            };
            Console.WriteLine(line);
            Console.ResetColor();
            _logFile?.WriteLine(line);
        }
    }
}
