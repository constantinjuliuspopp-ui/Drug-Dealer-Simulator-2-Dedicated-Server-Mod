using System.Globalization;

namespace DDS2Mod.Shared;

/// <summary>server.cfg (key=value, # Kommentare) plus Überschreibung per Kommandozeile.</summary>
public sealed class ServerConfig
{
    public string ServerName { get; set; } = "Mein DDS2 Server";
    public int Port { get; set; } = ModInfo.DefaultPort;
    /// <summary>Mitspieler ohne den Server selbst (der Server ist im Spiel technisch der Host und belegt einen Platz mehr).</summary>
    public int MaxPlayers { get; set; } = 2;
    public string Password { get; set; } = "";
    public string World { get; set; } = "";
    public int AutosaveMinutes { get; set; } = 10;
    /// <summary>Spiel ohne Fenster/Grafik starten; scheitert das, startet der Server es einmal mit kleinem Fenster.</summary>
    public bool Headless { get; set; } = true;
    /// <summary>0 = Spielport + 1.</summary>
    public int QueryPort { get; set; }
    /// <summary>Spiel nach einem Absturz automatisch neu starten.</summary>
    public bool AutoRestart { get; set; } = true;
    /// <summary>Spiel-Takte pro Sekunde (t.MaxFPS), 0 = unbegrenzt. Ohne Grafik spart das viel CPU.</summary>
    public int TickRate { get; set; } = 30;
    /// <summary>Steam unsichtbar starten, falls es nicht läuft, und auf die Anmeldung warten.</summary>
    public bool StartSteam { get; set; } = true;
    /// <summary>Figur des Server-Kontos unsichtbar machen, parken und für NPCs/Polizei abmelden.</summary>
    public bool HideServerCharacter { get; set; } = true;
    /// <summary>Ohne Mitspieler die Welt anhalten (Pause), damit Zeit, Wirtschaft und NPCs nicht weiterlaufen.</summary>
    public bool FreezeWhenEmpty { get; set; } = true;
    /// <summary>So lange nach dem letzten Mitspieler warten, dann speichern und einfrieren.</summary>
    public int FreezeDelaySeconds { get; set; } = 30;
    public string GameDir { get; set; } = "";

    public int EffectiveQueryPort => QueryPort > 0 ? QueryPort : ServerQuery.DefaultQueryPort(Port);

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

    /// <summary>--port 7777 --maxplayers 4 --world "X" --password Y --headless / --windowed</summary>
    public void ApplyArgs(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (!a.StartsWith("--")) continue;
            var key = a[2..].ToLowerInvariant();
            if (key is "server" or "config") { if (key == "config") i++; continue; }
            if (key == "headless") { Headless = true; continue; }
            if (key == "windowed") { Headless = false; continue; }
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
            case "maxplayers": MaxPlayers = ParseInt(value, 1, 15, where); break;
            case "password": Password = value; break;
            case "world": World = value; break;
            case "autosaveminutes": AutosaveMinutes = ParseInt(value, 0, 1440, where); break;
            case "headless": Headless = ParseBool(value, where); break;
            case "queryport": QueryPort = ParseInt(value, 0, 65535, where); break;
            case "autorestart": AutoRestart = ParseBool(value, where); break;
            case "tickrate": TickRate = value == "0" ? 0 : ParseInt(value, 10, 240, where); break;
            case "startsteam": StartSteam = ParseBool(value, where); break;
            case "hideservercharacter": HideServerCharacter = ParseBool(value, where); break;
            case "freezewhenempty": FreezeWhenEmpty = ParseBool(value, where); break;
            case "freezedelayseconds": FreezeDelaySeconds = ParseInt(value, 0, 3600, where); break;
            case "gamedir": GameDir = value; break;
            default: throw new FormatException($"{where}: unbekannte Einstellung '{key}'");
        }
    }

    static bool ParseBool(string value, string where) => value.ToLowerInvariant() switch
    {
        "1" or "true" or "ja" or "an" => true,
        "0" or "false" or "nein" or "aus" or "" => false,
        _ => throw new FormatException($"{where}: '{value}' ist weder true noch false"),
    };

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
