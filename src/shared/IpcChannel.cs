using System.Text;

namespace DDS2Mod.Shared;

public sealed record ModEvent(string Time, string Level, string Text);

public sealed record ModStatus(string Version, string Role, string Mode, DateTimeOffset Time, IReadOnlyList<(string Name, int Ping)> Players, string Place = "")
{
    public bool IsStale => DateTimeOffset.UtcNow - Time > TimeSpan.FromSeconds(10);
}

/// <summary>Dateibasierte Kommunikation mit der DDS2Net-Lua-Mod (Gegenstück zu main.lua).</summary>
public sealed class IpcChannel
{
    readonly string _commands, _events, _status;
    long _eventsPos;

    public string Dir { get; }

    public IpcChannel(string dir)
    {
        Dir = dir;
        Directory.CreateDirectory(dir);
        _commands = Path.Combine(dir, "commands.txt");
        _events = Path.Combine(dir, "events.log");
        _status = Path.Combine(dir, "status.txt");
    }

    /// <summary>Vor jedem Spielstart: alte Befehle/Ereignisse verwerfen.</summary>
    public void Reset()
    {
        foreach (var f in new[] { _commands, _events, _status })
            if (File.Exists(f)) File.Delete(f);
        _eventsPos = 0;
    }

    public void Send(string command)
    {
        using var fs = new FileStream(_commands, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        var bytes = Encoding.UTF8.GetBytes(command.Replace('\n', ' ') + "\n");
        fs.Write(bytes);
    }

    public List<ModEvent> ReadNewEvents()
    {
        var result = new List<ModEvent>();
        if (!File.Exists(_events)) return result;
        using var fs = new FileStream(_events, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (fs.Length < _eventsPos) _eventsPos = 0;
        if (fs.Length == _eventsPos) return result;
        fs.Seek(_eventsPos, SeekOrigin.Begin);
        var buffer = new byte[fs.Length - _eventsPos];
        var read = fs.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        var lastNewline = read == 0 ? -1 : Array.LastIndexOf(buffer, (byte)'\n', read - 1);
        if (lastNewline < 0) return result;
        _eventsPos += lastNewline + 1;

        foreach (var line in Encoding.UTF8.GetString(buffer, 0, lastNewline).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.TrimEnd('\r').Split('|', 3);
            if (parts.Length == 3) result.Add(new ModEvent(parts[0], parts[1], parts[2]));
        }
        return result;
    }

    public ModStatus? ReadStatus()
    {
        string[] lines;
        try { lines = File.ReadAllLines(_status); }
        catch (IOException) { return null; }

        string version = "", role = "", mode = "", place = "";
        long time = 0;
        var players = new List<(string, int)>();
        foreach (var line in lines)
        {
            var eq = line.IndexOf('=');
            if (eq < 0) continue;
            var key = line[..eq];
            var value = line[(eq + 1)..];
            switch (key)
            {
                case "version": version = value; break;
                case "role": role = value; break;
                case "mode": mode = value; break;
                case "place": place = value; break;
                case "time": long.TryParse(value, out time); break;
                case "player":
                    var sep = value.LastIndexOf('|');
                    var name = sep < 0 ? value : value[..sep];
                    var ping = sep < 0 ? -1 : (int.TryParse(value[(sep + 1)..], out var p) ? p : -1);
                    players.Add((name, ping));
                    break;
            }
        }
        return mode == "" ? null : new ModStatus(version, role, mode, DateTimeOffset.FromUnixTimeSeconds(time), players, place);
    }
}
