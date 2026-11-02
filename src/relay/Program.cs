using System.Globalization;
using System.Text;
using System.Text.Json;
using DDS2Mod.Relay;
using DDS2Mod.Shared;

// DDS2Relay [--config relay.cfg] [--gameport 7777] [--queryport 7778] [--agentport 7779] [--maxclients 8] [--secret X]
Console.OutputEncoding = Encoding.UTF8;
var version = ReadVersion();
if (args.Any(a => a is "--help" or "-h" or "/?"))
{
    Console.WriteLine($"""
        DDS2Relay {version} – Relay für die DDS2 Multiplayer Mod (ohne Spieldateien, ohne Steam)

        DDS2Relay [--config relay.cfg] [--gameport 7777] [--queryport 7778] [--agentport 7779]
                  [--maxclients 8] [--secret <Schlüssel>] [--bind 0.0.0.0]

        Ohne --config wird relay.cfg im aktuellen Ordner benutzt (und beim ersten Start angelegt).
        Ports (alle UDP): gameport + queryport für Mitspieler, agentport für den Host.
        Befehle in der Konsole: status, stop
        """);
    return 0;
}

var cfgIndex = Array.FindIndex(args, a => a == "--config");
var cfgPath = Path.GetFullPath(cfgIndex >= 0 && cfgIndex + 1 < args.Length ? args[cfgIndex + 1] : "relay.cfg");
var logDir = Path.Combine(Path.GetDirectoryName(cfgPath)!, "logs");
Directory.CreateDirectory(logDir);
using var logFile = new StreamWriter(Path.Combine(logDir, $"relay-{DateTime.Now:yyyyMMdd-HHmmss}.log"), append: true, new UTF8Encoding(false)) { AutoFlush = true };
var logLock = new object();
void Log(string level, string message)
{
    var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}";
    lock (logLock)
    {
        Console.WriteLine(line);
        logFile.WriteLine(line);
    }
}

try
{
    var o = new RelayOptions();
    if (File.Exists(cfgPath)) LoadConfig(cfgPath, o);
    ApplyArgs(args, o);
    if (string.IsNullOrWhiteSpace(o.Secret))
    {
        o.Secret = RelayProtocol.NewSecret();
        SaveSecret(cfgPath, o.Secret);
        Log("INFO", $"Neuer Relay-Schlüssel erzeugt und in {cfgPath} gespeichert.");
    }

    using var relay = new RelayServer(o, Log);
    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
    AppDomain.CurrentDomain.ProcessExit += (_, _) => cts.Cancel();

    Log("INFO", $"DDS2Relay {version} läuft. Ports (UDP): Spiel {relay.GamePort}, Abfrage {relay.QueryPort}, Host-Anmeldung {relay.AgentPort}. "
        + $"Max. {o.MaxClients} Mitspieler.");
    Log("INFO", $"Relay-Schlüssel für den Host: {o.Secret}");
    Log("INFO", "Der Host trägt in DDS2 Multiplayer Relay-Adresse und Schlüssel ein; Mitspieler verbinden sich mit dieser Adresse auf Port "
        + relay.GamePort + ".");

    var run = relay.RunAsync(cts.Token);
    if (!Console.IsInputRedirected)
    {
        new Thread(() =>
        {
            while (!cts.IsCancellationRequested)
            {
                var line = Console.ReadLine();
                if (line is null) return;
                switch (line.Trim().ToLowerInvariant())
                {
                    case "status":
                        Log("INFO", relay.AgentConnected
                            ? $"Host angemeldet ({relay.AgentAddress}), {relay.ClientCount} Mitspieler."
                            : "Kein Host angemeldet.");
                        break;
                    case "stop" or "quit" or "exit": cts.Cancel(); break;
                    case "": break;
                    default: Log("WARN", "Befehle: status, stop"); break;
                }
            }
        }) { IsBackground = true }.Start();
    }
    try { await Task.Delay(Timeout.Infinite, cts.Token); } catch (OperationCanceledException) { }
    await run;
    Log("INFO", "Relay gestoppt.");
    return 0;
}
catch (Exception ex) when (ex is FormatException or ArgumentException or System.Net.Sockets.SocketException or IOException)
{
    Log("FEHLER", ex is System.Net.Sockets.SocketException
        ? $"Port belegt oder nicht erlaubt: {ex.Message}"
        : ex.Message);
    return 1;
}

static string ReadVersion()
{
    using var s = typeof(RelayServer).Assembly.GetManifestResourceStream("DDS2Mod.version.json");
    if (s is null) return "?";
    using var doc = JsonDocument.Parse(s);
    return doc.RootElement.GetProperty("modVersion").GetString() ?? "?";
}

static void Set(RelayOptions o, string key, string value, string where)
{
    int Port() => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) && p is >= 0 and <= 65535
        ? p : throw new FormatException($"{where}: '{value}' ist kein Port");
    switch (key.ToLowerInvariant())
    {
        case "gameport": o.GamePort = Port(); break;
        case "queryport": o.QueryPort = Port(); break;
        case "agentport": o.AgentPort = Port(); break;
        case "maxclients": o.MaxClients = Math.Clamp(Port(), 1, 64); break;
        case "secret": o.Secret = value; break;
        case "bind": o.BindAddress = value; break;
        case "config": break;
        default: throw new FormatException($"{where}: unbekannte Einstellung '{key}'");
    }
}

static void LoadConfig(string path, RelayOptions o)
{
    var n = 0;
    foreach (var raw in File.ReadAllLines(path))
    {
        n++;
        var line = raw.Trim();
        if (line.Length == 0 || line.StartsWith('#')) continue;
        var eq = line.IndexOf('=');
        if (eq < 0) throw new FormatException($"relay.cfg Zeile {n}: '=' fehlt");
        Set(o, line[..eq].Trim(), line[(eq + 1)..].Trim(), $"relay.cfg Zeile {n}");
    }
}

static void ApplyArgs(string[] args, RelayOptions o)
{
    for (var i = 0; i < args.Length; i++)
    {
        if (!args[i].StartsWith("--")) continue;
        if (i + 1 >= args.Length) throw new FormatException($"Wert für {args[i]} fehlt");
        Set(o, args[i][2..], args[++i], args[i - 1]);
    }
}

static void SaveSecret(string path, string secret)
{
    var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : new List<string>
    {
        "# DDS2Relay – Konfiguration (alle Ports UDP)",
        "GamePort=7777",
        "QueryPort=7778",
        "AgentPort=7779",
        "MaxClients=8",
    };
    var i = lines.FindIndex(l => l.TrimStart().StartsWith("Secret", StringComparison.OrdinalIgnoreCase));
    if (i >= 0) lines[i] = "Secret=" + secret;
    else lines.Add("Secret=" + secret);
    File.WriteAllLines(path, lines, new UTF8Encoding(false));
}
