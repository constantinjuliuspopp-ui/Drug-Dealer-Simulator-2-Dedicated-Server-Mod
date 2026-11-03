using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using DDS2Mod.Shared;

namespace DDS2Mod.Server;

/// <summary>
/// DDS2Server.exe: startet das Spiel ohne Fenster als Koop-Host ("Dedicated Server" aus den Spieldateien),
/// lädt die Welt über die DDS2Net-Mod, beantwortet den Abfrage-Port und nimmt Konsolenbefehle entgegen.
/// Voraussetzung (Kopierschutz des Spiels): Steam-Client läuft und ist mit einem Konto angemeldet, dem das Spiel gehört.
/// </summary>
public static class ServerMode
{
    const int KeepLogs = 30;
    static readonly TimeSpan HeadlessStartTimeout = TimeSpan.FromMinutes(3);
    static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(60);

    static StreamWriter? _logFile;
    static readonly object LogLock = new();

    // Von mehreren Threads benutzt (Konsole, Ereignis-Pumpe, Abfrage-Port, Hauptschleife).
    static volatile Process? _game;
    static volatile ModStatus? _status;
    static volatile bool _stopping;
    static long _stopRequestedTicks;
    static IpcChannel? _ipc;
    static ServerConfig _cfg = new();
    static string _worldName = "";
    static string _gameBuild = "";
    static readonly DateTime StartedAt = DateTime.Now;

    [DllImport("kernel32.dll")] static extern bool SetConsoleCtrlHandler(ConsoleCtrlHandler handler, bool add);
    delegate bool ConsoleCtrlHandler(int ctrlType);
    static readonly ConsoleCtrlHandler CloseHandler = OnConsoleClose; // Referenz halten (GC)

    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;
        Console.Title = $"DDS2 Server {ModInfo.Version}";
        if (args.Any(a => a is "--help" or "-h" or "/?"))
        {
            PrintUsage();
            return 0;
        }

        try
        {
            return RunServer(args);
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or IOException or SocketException)
        {
            Log("FEHLER", ex.Message);
            if (!Console.IsInputRedirected)
            {
                Console.WriteLine("Enter drücken zum Schließen.");
                Console.ReadLine();
            }
            return 1;
        }
        finally
        {
            _logFile?.Dispose();
        }
    }

    static void PrintUsage() => Console.WriteLine("""
        DDS2Server.exe [--config <server.cfg>] [--port 7777] [--maxplayers 2] [--world "Name"]
                       [--password X] [--queryport 7778] [--headless | --windowed]

        Ohne --config: server.cfg im aktuellen Ordner, sonst Dokumente\DDS2 Server\server.cfg.
        Befehle in der Konsole: status, players, kick <Name>, save, stop, help
        """);

    static string DefaultServerDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DDS2 Server");

    static string FindConfig(string[] args)
    {
        var fromArgs = ServerConfig.ConfigPathFromArgs(args);
        if (fromArgs != null) return Path.GetFullPath(fromArgs);
        var local = Path.GetFullPath("server.cfg");
        return File.Exists(local) ? local : Path.Combine(DefaultServerDir, "server.cfg");
    }

    static int RunServer(string[] args)
    {
        var cfgPath = FindConfig(args);
        var serverDir = Path.GetDirectoryName(cfgPath)!;
        OpenLog(serverDir);

        _cfg = ServerConfig.Load(cfgPath);
        _cfg.ApplyArgs(args);
        var cfg = _cfg;
        Log("INFO", $"DDS2 Server {ModInfo.Version} – Konfiguration: {(File.Exists(cfgPath) ? cfgPath : cfgPath + " (fehlt, Standardwerte)")}");
        Log("INFO", $"Name '{cfg.ServerName}', Spielport {cfg.Port}/UDP, Abfrage-Port {cfg.EffectiveQueryPort}/UDP, "
            + $"max. {cfg.MaxPlayers} Mitspieler, Passwort {(cfg.Password.Length > 0 ? "an" : "aus")}, "
            + $"{(cfg.Headless ? "ohne Fenster" : "mit Fenster")}, Autosave {(cfg.AutosaveMinutes > 0 ? cfg.AutosaveMinutes + " min" : "aus")}, "
            + $"Takt {(cfg.TickRate > 0 ? cfg.TickRate + "/s" : "unbegrenzt")}");
        if (cfg.MaxPlayers > 2)
            Log("WARN", "Das Spiel ist für 3 Spieler ausgelegt (Server + 2). Mehr Mitspieler sind experimentell.");

        var gameDir = string.IsNullOrEmpty(cfg.GameDir) ? GameLocator.FindGameDir() : cfg.GameDir;
        if (gameDir is null || !GameLocator.IsGameDir(gameDir))
            throw new InvalidOperationException("Spielordner nicht gefunden. In server.cfg 'GameDir=' setzen (Ordner mit DrugDealerSimulator2.exe).");
        if (!GameProcess.IsModInstalled(gameDir))
            throw new InvalidOperationException($"Die Mod ist in {gameDir} nicht installiert. Bitte den Installer ausführen (Art 'Dedicated Server').");
        EnsureSteam(cfg);
        if (GameLocator.IsGameRunning())
            throw new InvalidOperationException("Drug Dealer Simulator 2 läuft auf diesem PC schon. Bitte zuerst beenden – der Server startet seine eigene Instanz.");
        _gameBuild = GameLocator.ReadBuildId(gameDir);
        Log("INFO", $"Spielordner: {gameDir}{(_gameBuild.Length > 0 ? $" (Build {_gameBuild})" : "")}");

        var backup = SaveBackup.Create();
        if (backup != null) Log("INFO", $"Spielstände gesichert: {backup}");
        // Der Server ist im Spiel selbst ein Spieler (Host): ein Platz mehr als Mitspieler.
        IniOverride.Apply(new OverrideSettings(cfg.Port, cfg.MaxPlayers + 1, cfg.Password));
        Log("INFO", "Netzwerk-Einstellungen gesetzt (Direkt-IP, Port, Spielerlimit, Versionscheck).");

        var cartels = SaveGames.ListCartels();
        Log("INFO", cartels.Count > 0
            ? $"Spielstände: {string.Join(", ", cartels.Select(c => c.Name == c.Alias ? c.Alias : $"{c.Name} [{c.Alias}]"))}"
            : "Keine Spielstände gefunden. Lege im Spiel einmal eine Welt an oder kopiere einen Spielstand auf diesen PC (siehe README).");
        var world = SaveGames.Resolve(cfg.World, cartels, out var warning);
        if (warning != null) Log("WARN", warning);
        _worldName = world?.Name ?? "";

        _ipc = new IpcChannel(Path.Combine(serverDir, "ipc"));
        using var cts = new CancellationTokenSource();
        using var query = new ServerQuery.Responder(cfg.EffectiveQueryPort, cfg.Password, CurrentInfo);
        var queryTask = query.RunAsync(cts.Token);
        Log("INFO", $"Abfrage-Port {query.Port}/UDP bereit (Ping, Spielerzahl, Version, Passwortprüfung).");

        Console.CancelKeyPress += (_, e) => { e.Cancel = true; RequestStop("Strg+C"); };
        SetConsoleCtrlHandler(CloseHandler, true);
        new Thread(ReadCommands) { IsBackground = true, Name = "Konsole" }.Start();
        var pump = Task.Run(() => PumpEvents(cts.Token));

        var exitCode = RunGameLoop(gameDir, world, cfg);

        cts.Cancel();
        pump.Wait();
        try { queryTask.Wait(2000); } catch (AggregateException) { }
        if (!GameLocator.IsGameRunning()) IniOverride.RestoreAll();
        Log("INFO", "Server gestoppt.");
        return exitCode;
    }

    /// <summary>
    /// Das Spiel braucht einen laufenden, angemeldeten Steam-Client (Kopierschutz des Spiels). Läuft Steam nicht,
    /// wird es unsichtbar gestartet (-silent); angemeldet wird es mit der gespeicherten Anmeldung ("Angemeldet bleiben").
    /// </summary>
    static void EnsureSteam(ServerConfig cfg)
    {
        if (GameLocator.IsSteamRunning()) return;
        var steam = GameLocator.SteamExe();
        if (!cfg.StartSteam || steam is null)
            throw new InvalidOperationException("Steam läuft nicht. Das Spiel startet nur mit laufendem, angemeldetem Steam-Client "
                + "(Konto, dem das Spiel gehört). Steam starten, anmelden, dann den Server erneut starten.");
        Log("INFO", "Steam läuft nicht – starte Steam im Hintergrund und warte auf die Anmeldung ...");
        Process.Start(new ProcessStartInfo(steam, "-silent") { UseShellExecute = true });
        // Steam meldet die Anmeldung nicht überall in der Registry (ActiveUser bleibt teils 0): höchstens 60 s warten.
        var until = DateTime.Now.AddSeconds(60);
        while (DateTime.Now < until && !GameLocator.IsSteamLoggedIn()) Thread.Sleep(2000);
        Log("INFO", GameLocator.IsSteamLoggedIn() ? "Steam ist angemeldet." : "Steam läuft – Anmeldung nicht erkennbar, versuche es trotzdem.");
        Thread.Sleep(5000); // Steam braucht nach der Anmeldung einen Moment, bis Spiele starten können
    }

    /// <summary>Startet das Spiel, wartet auf das Ende, startet nach Absturz neu oder wechselt auf Fenster-Modus.</summary>
    static int RunGameLoop(string gameDir, Cartel? world, ServerConfig cfg)
    {
        var headless = cfg.Headless;
        var crashes = new List<DateTime>();
        while (true)
        {
            _ipc!.Reset();
            _status = null;
            var gameArgs = headless
                ? new List<string> { "-nullrhi", "-nosound", "-nosplash" }
                : new List<string> { "-windowed", "-ResX=800", "-ResY=450", "-nosplash" };
            var env = new Dictionary<string, string>();
            if (world != null) env["DDS2_WORLD"] = world.Alias;
            if (cfg.TickRate > 0) env["DDS2_MAX_FPS"] = cfg.TickRate.ToString();
            if (cfg.HideServerCharacter) env["DDS2_HIDE_HOST"] = "1";

            var started = DateTime.Now;
            var game = _game = GameProcess.Launch(gameDir, "server", _ipc.Dir, gameArgs, env);
            Log("INFO", $"Spiel gestartet ({(headless ? "ohne Fenster" : "mit Fenster")}, PID {game.Id}).");
            Log("INFO", world != null
                ? $"Die Welt '{world.Name}' wird automatisch geladen, sobald das Hauptmenü erreicht ist."
                : "Keine Welt zum automatischen Laden – 'load <Name>' eingeben, sobald das Hauptmenü erreicht ist.");

            var sawStatus = false;
            var killedForFallback = false;
            while (!game.WaitForExit(1000))
            {
                sawStatus |= _status != null;
                var stopAt = Interlocked.Read(ref _stopRequestedTicks);
                if (stopAt != 0 && DateTime.UtcNow - new DateTime(stopAt, DateTimeKind.Utc) > StopTimeout)
                {
                    Log("WARN", "Spiel reagiert nicht auf 'stop' – wird beendet.");
                    game.Kill();
                }
                else if (headless && !sawStatus && !_stopping && DateTime.Now - started > HeadlessStartTimeout)
                {
                    Log("WARN", $"Ohne Fenster kam nach {HeadlessStartTimeout.TotalMinutes:0} min kein Lebenszeichen der Mod – beende das Spiel.");
                    game.Kill();
                    killedForFallback = true;
                }
            }
            sawStatus |= _status != null;
            Thread.Sleep(1500); // letzte Ereignisse der Mod (z. B. SHUTDOWN) noch einlesen lassen
            Log(_stopping ? "INFO" : "WARN", $"Spiel beendet (Exit-Code {game.ExitCode}).");
            _game = null;

            if (_stopping) return 0;
            Thread.Sleep(5000); // Startet Steam das Spiel selbst neu, taucht der neue Prozess erst nach ein paar Sekunden auf.
            if (GameLocator.IsGameRunning())
            {
                Log("FEHLER", "Steam hat das Spiel selbst neu gestartet – diese Instanz kennt die Server-Einstellungen nicht. "
                    + "Bitte das Spiel beenden und den Server erneut starten. Steam muss vorher laufen.");
                return 1;
            }
            if (headless && !sawStatus)
            {
                Log("WARN", killedForFallback || DateTime.Now - started < HeadlessStartTimeout
                    ? "Der Start ohne Fenster hat nicht geklappt. Neuer Versuch mit kleinem Fenster. "
                      + "Tipp: in server.cfg 'Headless=false' setzen, wenn das immer so ist."
                    : "Neuer Versuch mit kleinem Fenster.");
                headless = false;
                continue;
            }

            crashes.RemoveAll(t => DateTime.Now - t > TimeSpan.FromMinutes(10));
            crashes.Add(DateTime.Now);
            if (!cfg.AutoRestart || crashes.Count > 3)
            {
                Log("FEHLER", cfg.AutoRestart
                    ? "Das Spiel ist in 10 Minuten mehr als dreimal abgestürzt – kein weiterer Neustart. Logs prüfen."
                    : "Das Spiel wurde unerwartet beendet (AutoRestart=false).");
                return 1;
            }
            Log("WARN", "Unerwartetes Ende – Neustart in 10 Sekunden (Strg+C bricht ab) ...");
            for (var i = 0; i < 10 && !_stopping; i++) Thread.Sleep(1000);
            if (_stopping) return 0;
        }
    }

    static ServerInfo CurrentInfo()
    {
        var s = _status;
        var state = s is null || s.IsStale ? "starting"
            : s.Mode == "host" ? s.Place
            : s.Place == "world" ? "singleplayer"
            : s.Place;
        var players = s is { Mode: "host", IsStale: false } ? s.Others.Count() : 0;
        return new ServerInfo(_cfg.ServerName, ModInfo.Version, ServerQuery.ProtocolVersion, _gameBuild, state,
            players, _cfg.MaxPlayers, _cfg.Password.Length > 0, _worldName);
    }

    static void RequestStop(string reason)
    {
        if (_stopping) return;
        _stopping = true;
        Log("INFO", $"{reason} – speichere und beende ...");
        Interlocked.Exchange(ref _saveRequestedTicks, DateTime.UtcNow.Ticks);
        Interlocked.Exchange(ref _stopRequestedTicks, DateTime.UtcNow.Ticks);
        if (_game is { HasExited: false }) _ipc?.Send("stop");
    }

    // Fenster geschlossen / Abmelden: Windows gibt nur wenige Sekunden. Die Mod speichert und beendet das Spiel selbst.
    static bool OnConsoleClose(int ctrlType)
    {
        if (ctrlType is 2 or 5 or 6) // CLOSE, LOGOFF, SHUTDOWN
        {
            RequestStop("Konsole wird geschlossen");
            Thread.Sleep(1500);
        }
        return false;
    }

    // Zeitpunkt der letzten save-Anforderung (UTC-Ticks, 0 = keine offen).
    static long _saveRequestedTicks;

    static void RequestSave()
    {
        _ipc!.Send("save");
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

    static async Task PumpEvents(CancellationToken ct)
    {
        var cfg = _cfg;
        var nextAutosave = cfg.AutosaveMinutes > 0 ? DateTime.Now.AddMinutes(cfg.AutosaveMinutes) : DateTime.MaxValue;
        var knownPlayers = new HashSet<string>();
        var lastState = "";
        while (!ct.IsCancellationRequested)
        {
            foreach (var e in _ipc!.ReadNewEvents())
            {
                if (e.Level == "SHUTDOWN")
                {
                    // Die Mod beendet das Spiel gewollt (stop über die Mod, quit) – kein Absturz, kein Neustart.
                    _stopping = true;
                    Interlocked.CompareExchange(ref _stopRequestedTicks, DateTime.UtcNow.Ticks, 0);
                    Log("INFO", e.Text);
                    continue;
                }
                Log(e.Level, ErrorTexts.IsError(e) ? ErrorTexts.Describe(e) : e.Text);
            }

            var status = _ipc.ReadStatus();
            if (status is { IsStale: false })
            {
                _status = status;
                var state = $"{status.Mode}/{status.Place}";
                if (state != lastState)
                {
                    Log(status.Mode == "client" ? "WARN" : "INFO", (status.Mode, status.Place) switch
                    {
                        ("client", _) => "Diese Instanz ist als Client verbunden, nicht als Host.",
                        (_, "menu") => "Spiel im Hauptmenü.",
                        ("host", "lobby") => $"Lobby läuft als Host – Spieler können sich jetzt auf Port {cfg.Port} verbinden.",
                        ("host", "world") => $"Welt läuft – Spieler können sich auf Port {cfg.Port} verbinden.",
                        ("standalone", "world") => "Welt läuft im Einzelspieler-Modus – Mitspieler können NICHT beitreten. Spielstand als Koop laden.",
                        _ => $"Status: {status.Mode} / {status.Place}",
                    });
                    lastState = state;
                }
                if (status.Mode == "host") TrackPlayers(status, knownPlayers);
            }

            if (DateTime.Now >= nextAutosave)
            {
                if (_status is { Mode: "host", Place: "world", IsStale: false }) { Log("INFO", "Autosave ..."); RequestSave(); }
                nextAutosave = DateTime.Now.AddMinutes(cfg.AutosaveMinutes);
            }
            CheckSaveWritten();

            try { await Task.Delay(1000, ct); } catch (TaskCanceledException) { break; }
        }
    }

    static void TrackPlayers(ModStatus status, HashSet<string> known)
    {
        var current = status.Others.Select(p => p.Name).ToHashSet();
        foreach (var joined in current.Except(known)) Log("INFO", $"Spieler verbunden: {joined}");
        foreach (var left in known.Except(current)) Log("INFO", $"Spieler getrennt: {left}");
        known.Clear();
        known.UnionWith(current);
    }

    static void ReadCommands()
    {
        while (true)
        {
            var line = Console.ReadLine();
            if (line is null) return;
            var parts = line.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;
            var cmd = parts[0].ToLowerInvariant();
            if (_game is null && cmd is not ("help" or "status" or "stop"))
            {
                Log("WARN", "Das Spiel läuft gerade nicht (Start oder Neustart) – Befehl ignoriert.");
                continue;
            }
            switch (cmd)
            {
                case "status": PrintStatus(); break;
                case "players": PrintPlayers(); break;
                case "kick":
                    if (parts.Length < 2) Log("WARN", "Benutzung: kick <Spielername>");
                    else _ipc!.Send($"kick {parts[1]}");
                    break;
                case "save": RequestSave(); break;
                case "start": _ipc!.Send("start"); break;
                case "load":
                    if (parts.Length < 2) { Log("WARN", "Benutzung: load <Name des Spielstands>"); break; }
                    var target = SaveGames.Resolve(parts[1], SaveGames.ListCartels(), out var loadWarning);
                    if (target is null || !target.Alias.Equals(parts[1], StringComparison.OrdinalIgnoreCase)
                            && !target.Name.Equals(parts[1], StringComparison.OrdinalIgnoreCase))
                    {
                        Log("WARN", loadWarning ?? $"Kein Spielstand '{parts[1]}'.");
                        break;
                    }
                    _worldName = target.Name;
                    _ipc!.Send($"load {target.Alias}");
                    break;
                case "call" or "dump":
                    // Diagnose für die Weiterentwicklung (siehe docs/test-0.2.md)
                    _ipc!.Send(line.Trim());
                    break;
                case "stop" or "quit" or "exit": RequestStop("stop"); break;
                case "help":
                    Log("INFO", "status – Zustand, Ports, Laufzeit | players – Mitspieler mit Ping | kick <Name> – Spieler entfernen | "
                        + "save – speichern | stop – speichern und beenden | load <Welt> – Welt laden (im Hauptmenü) | start – Spiel aus der Lobby starten");
                    break;
                default: Log("WARN", $"Unbekannter Befehl '{parts[0]}' – 'help' für Hilfe"); break;
            }
        }
    }

    static void PrintStatus()
    {
        var i = CurrentInfo();
        var up = DateTime.Now - StartedAt;
        Log("INFO", $"Zustand: {StateText(i.State)} | Welt: {(i.World.Length > 0 ? i.World : "?")} | Mitspieler: {i.Players}/{i.MaxPlayers} | "
            + $"Ports: Spiel {_cfg.Port}/UDP, Abfrage {_cfg.EffectiveQueryPort}/UDP | Laufzeit: {(int)up.TotalHours}:{up.Minutes:00} h");
        var s = _status;
        if (s != null && s.Version != ModInfo.Version)
            Log("WARN", $"Mod im Spiel hat Version {s.Version}, Server {ModInfo.Version} – bitte neu installieren.");
    }

    static void PrintPlayers()
    {
        var s = _status;
        if (s is not { Mode: "host", IsStale: false })
        {
            Log("INFO", $"Keine Spielerliste ({StateText(CurrentInfo().State)}).");
            return;
        }
        var others = s.Others.ToList();
        Log("INFO", others.Count == 0 ? "Keine Mitspieler verbunden." : $"{others.Count} Mitspieler:");
        foreach (var (name, ping) in others)
            Log("INFO", $"  {name}  ({(ping >= 0 ? ping + " ms" : "Ping ?")})");
    }

    static string StateText(string state) => state switch
    {
        "starting" => "Spiel startet",
        "menu" => "Hauptmenü",
        "lobby" => "Lobby (beitretbar)",
        "world" => "Welt läuft (beitretbar)",
        "singleplayer" => "Einzelspieler (nicht beitretbar)",
        _ => state,
    };

    static void OpenLog(string serverDir)
    {
        var logDir = Path.Combine(serverDir, "logs");
        Directory.CreateDirectory(logDir);
        foreach (var old in Directory.GetFiles(logDir, "server-*.log").OrderByDescending(f => f).Skip(KeepLogs - 1))
            File.Delete(old);
        _logFile = new StreamWriter(Path.Combine(logDir, $"server-{DateTime.Now:yyyyMMdd-HHmmss}.log"), append: true, new UTF8Encoding(false)) { AutoFlush = true };
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
