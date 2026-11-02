using System.Net;

namespace DDS2Mod.Shared;

public enum JoinProblem { None, NoAnswer, Version, NotReady, Full, PasswordNeeded, WrongPassword }

public sealed record JoinVerdict(JoinProblem Problem, string Message, ServerInfo? Info)
{
    public bool Ok => Problem == JoinProblem.None;
}

/// <summary>Prüft vor dem Verbinden über den Abfrage-Port, ob der Beitritt klappen kann.</summary>
public static class JoinCheck
{
    public static async Task<JoinVerdict> RunAsync(IPEndPoint queryEndpoint, string ownGameBuild, string password, CancellationToken ct = default)
    {
        var info = await ServerQuery.QueryAsync(queryEndpoint, ct: ct);
        var verdict = Evaluate(info, ownGameBuild, password);
        if (verdict.Problem != JoinProblem.None || info is not { HasPassword: true }) return verdict;

        var ok = await ServerQuery.CheckPasswordAsync(queryEndpoint, info.Challenge, password, ct: ct);
        return ok switch
        {
            true => verdict,
            false => new(JoinProblem.WrongPassword, "Falsches Passwort.", info),
            null => new(JoinProblem.NoAnswer, "Der Server hat auf die Passwortprüfung nicht geantwortet.", info),
        };
    }

    /// <summary>Alles außer der Passwortprüfung selbst (die braucht eine zweite Anfrage).</summary>
    public static JoinVerdict Evaluate(ServerInfo? info, string ownGameBuild, string password)
    {
        if (info is null)
            return new(JoinProblem.NoAnswer,
                "Zeitüberschreitung: Der Server antwortet nicht. Läuft er, stimmen IP und Port, sind die UDP-Ports freigegeben?", null);
        if (info.ModVersion != ModInfo.Version || info.Protocol != ServerQuery.ProtocolVersion)
            return new(JoinProblem.Version,
                $"Versionskonflikt: Der Server hat die Mod-Version {info.ModVersion}, du {ModInfo.Version}. Beide brauchen dieselbe Version.", info);
        if (info.GameBuild.Length > 0 && ownGameBuild.Length > 0 && info.GameBuild != ownGameBuild)
            return new(JoinProblem.Version,
                $"Versionskonflikt: Der Server hat Spiel-Build {info.GameBuild}, du {ownGameBuild}. Bitte das Spiel auf beiden Seiten über Steam aktualisieren.", info);
        if (!info.IsJoinable)
            return new(JoinProblem.NotReady, "Der Server lädt die Welt noch. Bitte in einer Minute erneut versuchen.", info);
        if (info.IsFull)
            return new(JoinProblem.Full, $"Der Server ist voll ({info.Players}/{info.MaxPlayers} Spieler).", info);
        if (info.HasPassword && password.Length == 0)
            return new(JoinProblem.PasswordNeeded, "Dieser Server ist passwortgeschützt. Bitte Passwort eingeben.", info);
        return new(JoinProblem.None, "", info);
    }
}
