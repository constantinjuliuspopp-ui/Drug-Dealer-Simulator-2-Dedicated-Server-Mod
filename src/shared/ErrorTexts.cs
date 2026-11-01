namespace DDS2Mod.Shared;

/// <summary>Übersetzt Engine-Fehlercodes aus der Mod in verständliche Meldungen.</summary>
public static class ErrorTexts
{
    public static string Describe(ModEvent e)
    {
        var code = e.Text.Split(' ')[0];
        return code switch
        {
            "ConnectionTimeout" => "Zeitüberschreitung: Der Server antwortet nicht. IP/Port prüfen, läuft der Server, ist UDP-Port freigegeben?",
            "ConnectionLost" => "Verbindung zum Server verloren.",
            "OutdatedClient" or "OutdatedServer" =>
                "Abgelehnt: falsche Version oder falsches Passwort. Beide Seiten brauchen dieselbe Spiel- und Mod-Version.",
            "PendingConnectionFailure" => "Der Server hat die Verbindung abgelehnt (vermutlich voll).",
            "FailureReceived" => "Der Server hat die Verbindung beendet (z. B. gekickt oder Server gestoppt).",
            "NetDriverListenFailure" => "Hosten fehlgeschlagen: Der Port ist belegt oder blockiert.",
            "NetChecksumMismatch" or "NetGuidMismatch" => "Spieldaten von Client und Server passen nicht zusammen (Spiel-Updates prüfen).",
            "PendingNetGameCreateFailure" or "InvalidURL" => "Ungültige Serveradresse.",
            _ => $"{e.Level}: {e.Text}",
        };
    }

    public static bool IsError(ModEvent e) => e.Level is "NETERROR" or "TRAVELERROR" or "ERROR";
}
