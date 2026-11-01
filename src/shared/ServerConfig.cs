using System.Globalization;

namespace DDS2Mod.Shared;

/// <summary>server.cfg (key=value, # Kommentare) plus Überschreibung per Kommandozeile.</summary>
public sealed class ServerConfig
{
    public string ServerName { get; set; } = "Mein DDS2 Server";
    public int Port { get; set; } = ModInfo.DefaultPort;
    public int MaxPlayers { get; set; } = 3;
    public string Password { get; set; } = "";
    public string World { get; set; } = "MeineWelt";
    public int AutosaveMinutes { get; set; } = 10;
    public bool Headless { get; set; }
    public string GameDir { get; set; } = "";

    public static ServerConfig Load(string path)
    {
        var cfg = new ServerConfig();
        if (!File.Exists(path)) return cfg;
        var lineNo = 0;
        foreach (var raw in File.ReadAllLines(path))
        {
            lineNo++;
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';')) continue;
            var eq = line.IndexOf('=');
            if (eq < 0) throw new FormatException($"server.cfg Zeile {lineNo}: '=' fehlt");
            cfg.Set(line[..eq].Trim(), line[(eq + 1)..].Trim(), $"server.cfg Zeile {lineNo}");
        }
        return cfg;
    }

    /// <summary>--port 7777 --maxplayers 4 --world "X" --password Y --headless</summary>
    public void ApplyArgs(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (!a.StartsWith("--")) continue;
            var key = a[2..].ToLowerInvariant();
            if (key is "server" or "config") { if (key == "config") i++; continue; }
            if (key == "headless") { Headless = true; continue; }
            if (i + 1 >= args.Length) throw new FormatException($"Wert für {a} fehlt");
            Set(key, args[++i], a);
        }
    }

    void Set(string key, string value, string where)
    {
        switch (key.ToLowerInvariant())
        {
            case "servername": ServerName = value; break;
            case "port": Port = ParseInt(value, 1, 65535, where); break;
            case "maxplayers": MaxPlayers = ParseInt(value, 1, 16, where); break;
            case "password": Password = value; break;
            case "world": World = value; break;
            case "autosaveminutes": AutosaveMinutes = ParseInt(value, 0, 1440, where); break;
            case "headless": Headless = value is "1" or "true" or "True"; break;
            case "gamedir": GameDir = value; break;
            default: throw new FormatException($"{where}: unbekannte Einstellung '{key}'");
        }
    }

    static int ParseInt(string value, int min, int max, string where)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) || n < min || n > max)
            throw new FormatException($"{where}: '{value}' ist keine Zahl zwischen {min} und {max}");
        return n;
    }

    public static string? ConfigPathFromArgs(string[] args)
    {
        var i = Array.FindIndex(args, a => a.Equals("--config", StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
