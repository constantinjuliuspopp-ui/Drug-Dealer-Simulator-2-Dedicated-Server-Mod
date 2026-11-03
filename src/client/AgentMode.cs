using System.Diagnostics;
using System.Text;
using DDS2Mod.Shared;

namespace DDS2Mod.Client;

/// <summary>
/// Unsichtbarer Helfer für den normalen Spielstart (ohne Launcher-Fenster): Die Lua-Mod kann selbst keine
/// UDP-Abfragen senden. Die Mod startet beim Spielstart "dds2mod://agent"; dieser Modus hat kein Fenster,
/// beantwortet Beitrittsanfragen, fragt Ping/Spielerzahl der Favoriten ab, speichert Favoriten aus dem Spielmenü
/// und beendet sich, sobald das Spiel zu ist. Austausch über %APPDATA%\DDS2Mod\ipc-game.
/// </summary>
public static class AgentMode
{
    public static string IpcDir => Path.Combine(ModInfo.AppDataDir, "ipc-game");
    public const string HeartbeatFile = "agent.txt";
    public const string FavoriteRequestFile = "favorite-request.txt";

    public static int Run()
    {
        using var mutex = new Mutex(true, @"Local\DDS2ModAgent", out var first);
        if (!first) return 0; // läuft schon
        Directory.CreateDirectory(IpcDir);
        try { return RunAsync().GetAwaiter().GetResult(); }
        finally { TryDelete(Path.Combine(IpcDir, HeartbeatFile)); }
    }

    static async Task<int> RunAsync()
    {
        // Auf das Spiel warten (die Mod startet uns sehr früh), dann laufen, solange es läuft.
        var waitUntil = DateTime.Now.AddMinutes(2);
        while (!GameLocator.IsGameRunning())
        {
            if (DateTime.Now > waitUntil) return 0;
            await Task.Delay(1000);
        }
        var gameDir = GameLocator.FindGameDir();
        var build = gameDir is null ? "" : GameLocator.ReadBuildId(gameDir);
        var infos = new Dictionary<Favorite, ServerInfo?>();
        var nextRefresh = DateTime.MinValue;
        Task? refresh = null, join = null;

        while (GameLocator.IsGameRunning())
        {
            File.WriteAllText(Path.Combine(IpcDir, HeartbeatFile), DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());

            if (join is null or { IsCompleted: true } && JoinRequests.TryTake(IpcDir) is { } req)
                join = HandleJoinAsync(req, build);

            var refreshRequested = TakeFile(Path.Combine(IpcDir, JoinRequests.RefreshFile)) != null;
            if (ApplyFavoriteRequests(IpcDir)) { refreshRequested = true; infos.Clear(); }
            if ((refreshRequested || DateTime.Now >= nextRefresh) && refresh is null or { IsCompleted: true })
            {
                nextRefresh = DateTime.Now.AddSeconds(30);
                refresh = RefreshAsync(infos);
            }
            await Task.Delay(500);
        }
        return 0;
    }

    static async Task HandleJoinAsync(JoinRequest req, string build)
    {
        try
        {
            var fav = Favorites.Load().FirstOrDefault(f => f.Host.Equals(req.Host, StringComparison.OrdinalIgnoreCase) && f.Port == req.Port);
            // Ohne Launcher ist kein Passwort aktiv (Netzwerk-Version des Spiels unverändert).
            var r = await JoinRequests.EvaluateAsync(req, "", build, fav?.EffectiveQueryPort ?? ServerQuery.DefaultQueryPort(req.Port));
            if (r.Result == "restart")
                r = new JoinResponse("error", "Dieser Server hat ein Passwort. Passwort-Server bitte über \"DDS2 Multiplayer\" starten.");
            JoinRequests.Respond(IpcDir, req.Id, r);
        }
        catch (IOException) { }
    }

    static async Task RefreshAsync(Dictionary<Favorite, ServerInfo?> infos)
    {
        var list = Favorites.Load();
        await Task.WhenAll(list.Select(async f =>
        {
            ServerInfo? info = null;
            try
            {
                var address = (await System.Net.Dns.GetHostAddressesAsync(f.Host))
                    .FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                if (address != null)
                    info = await ServerQuery.QueryAsync(new System.Net.IPEndPoint(address, f.EffectiveQueryPort), attempts: 2);
            }
            catch (System.Net.Sockets.SocketException) { }
            lock (infos) infos[f] = info;
        }));
        try { lock (infos) Favorites.ExportForGame(list, infos); } catch (IOException) { }
    }

    /// <summary>Zeilen "add|Name|Host|Port" bzw. "remove|Host|Port" aus dem Spielmenü.</summary>
    public static bool ApplyFavoriteRequests(string ipcDir)
    {
        var text = TakeFile(Path.Combine(ipcDir, FavoriteRequestFile));
        if (text is null) return false;
        var list = Favorites.Load();
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var p = line.Split('|');
            if (p.Length == 4 && p[0] == "add" && int.TryParse(p[3], out var port) && p[2].Length > 0)
            {
                list.RemoveAll(f => f.Host.Equals(p[2], StringComparison.OrdinalIgnoreCase) && f.Port == port);
                list.Add(new Favorite(p[1].Length > 0 ? p[1] : p[2], p[2], port));
            }
            else if (p.Length == 3 && p[0] == "remove" && int.TryParse(p[2], out var rport))
                list.RemoveAll(f => f.Host.Equals(p[1], StringComparison.OrdinalIgnoreCase) && f.Port == rport);
        }
        list = list.OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        Favorites.Save(list);
        return true;
    }

    static string? TakeFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var s = File.ReadAllText(path, Encoding.UTF8);
            File.Delete(path);
            return s;
        }
        catch (IOException) { return null; }
    }

    static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { }
    }
}
