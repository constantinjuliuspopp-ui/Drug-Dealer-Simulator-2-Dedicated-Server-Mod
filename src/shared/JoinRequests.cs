using System.Net;
using System.Text;

namespace DDS2Mod.Shared;

public sealed record JoinRequest(string Id, string Host, int Port, string Password);

/// <summary>Antwort an das Spielmenü. NewPassword != null: Spiel muss mit diesem Passwort neu starten.</summary>
public sealed record JoinResponse(string Result, string Message, string? NewPassword = null);

/// <summary>
/// Beitrittsanfragen aus dem Menü im Spiel (menu.lua): join-request.txt "id|host|port|passwort" im IPC-Ordner,
/// Antwort join-response.txt "id|ergebnis|text" mit ergebnis ok, noanswer, password, restart oder error.
/// </summary>
public static class JoinRequests
{
    public const string RequestFile = "join-request.txt";
    public const string ResponseFile = "join-response.txt";
    public const string RefreshFile = "refresh-favorites.txt";

    /// <summary>Liest und löscht eine wartende Anfrage.</summary>
    public static JoinRequest? TryTake(string ipcDir)
    {
        var path = Path.Combine(ipcDir, RequestFile);
        string text;
        try
        {
            if (!File.Exists(path)) return null;
            text = File.ReadAllText(path);
            File.Delete(path);
        }
        catch (IOException) { return null; }
        return Parse(text);
    }

    internal static JoinRequest? Parse(string text)
    {
        var p = text.TrimEnd('\r', '\n').Split('|', 4);
        if (p.Length < 3 || p[1].Length == 0 || !int.TryParse(p[2], out var port) || port is < 1 or > 65535) return null;
        return new JoinRequest(p[0], p[1].Trim(), port, p.Length > 3 ? p[3] : "");
    }

    public static void Respond(string ipcDir, string id, JoinResponse r)
    {
        var tmp = Path.Combine(ipcDir, ResponseFile + ".tmp");
        File.WriteAllText(tmp, $"{id}|{r.Result}|{r.Message.Replace('\n', ' ').Replace('\r', ' ')}\n", new UTF8Encoding(false));
        File.Move(tmp, Path.Combine(ipcDir, ResponseFile), overwrite: true);
    }

    /// <summary>
    /// Prüft den Server. Das Passwort steckt in der Netzwerk-Version des Spiels und wird beim Start festgelegt:
    /// passt es nicht zum aktiven, muss das Spiel neu starten (Ergebnis "restart").
    /// </summary>
    public static async Task<JoinResponse> EvaluateAsync(JoinRequest req, string activePassword, string ownGameBuild,
        int queryPort, CancellationToken ct = default)
    {
        IPAddress? address;
        try
        {
            address = (await Dns.GetHostAddressesAsync(req.Host, ct)).FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
        }
        catch (System.Net.Sockets.SocketException) { address = null; }
        if (address is null) return new("error", $"Hostname '{req.Host}' wurde nicht gefunden.");

        var password = req.Password.Length > 0 ? req.Password : activePassword;
        var verdict = await JoinCheck.RunAsync(new IPEndPoint(address, queryPort), ownGameBuild, password, ct);
        switch (verdict.Problem)
        {
            case JoinProblem.None:
                var needed = verdict.Info is { HasPassword: true } ? password : "";
                if (verdict.Info != null && needed != activePassword)
                    return new("restart", "Das Passwort wird übernommen: Das Spiel startet neu und verbindet dann automatisch.", needed);
                return new("ok", verdict.Info?.Name ?? "");
            case JoinProblem.NoAnswer: return new("noanswer", verdict.Message);
            case JoinProblem.PasswordNeeded: return new("password", "Dieser Server ist passwortgeschützt. Bitte Passwort eingeben.");
            case JoinProblem.WrongPassword: return new("password", "Falsches Passwort.");
            default: return new("error", verdict.Message);
        }
    }

    /// <summary>Kurzer Zustand für die Favoritenliste im Spiel.</summary>
    public static string StateCode(ServerInfo? info, bool queried) =>
        info is null ? (queried ? "offline" : "")
        : info.ModVersion != ModInfo.Version ? "version"
        : !info.IsJoinable ? "starting"
        : info.HasPassword ? "pw"
        : "online";
}
