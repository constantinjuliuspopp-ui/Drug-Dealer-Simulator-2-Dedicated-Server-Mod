using System.Text;

namespace DDS2Mod.Shared;

/// <summary>Ein Spielstand ("Kartell"): Ordner SaveGames\Cartels\&lt;Alias&gt; mit Anzeigenamen.</summary>
public sealed record Cartel(string Alias, string Name, DateTime LastWrite);

/// <summary>Liest die Spielstände des Spiels (nur lesen) und findet den aus server.cfg.</summary>
public static class SaveGames
{
    public static string CartelsDir(string? savedDir = null) =>
        Path.Combine(savedDir ?? GameLocator.SavedDir, "SaveGames", "Cartels");

    public static List<Cartel> ListCartels(string? savedDir = null)
    {
        var dir = CartelsDir(savedDir);
        if (!Directory.Exists(dir)) return new();
        return new DirectoryInfo(dir).EnumerateDirectories()
            .Select(d => new Cartel(d.Name, ReadCustomName(Path.Combine(d.FullName, "CartelDefaults.sav")) ?? d.Name,
                d.EnumerateFiles().Select(f => f.LastWriteTime).DefaultIfEmpty(d.LastWriteTime).Max()))
            .OrderByDescending(c => c.LastWrite)
            .ToList();
    }

    /// <summary>
    /// Sucht den Spielstand zu World (Ordnername oder Anzeigename, Groß/Klein egal).
    /// Gibt es genau einen Spielstand, wird er auch ohne passenden Namen genommen.
    /// </summary>
    public static Cartel? Resolve(string world, List<Cartel> cartels, out string? warning)
    {
        warning = null;
        if (string.IsNullOrWhiteSpace(world))
        {
            if (cartels.Count == 0) warning = "Keine Spielstände gefunden. Zuerst im Spiel unter 'Neues Kartell' eine Welt anlegen.";
            return cartels.FirstOrDefault(); // zuletzt gespielt (Liste ist danach sortiert)
        }
        var match = cartels.FirstOrDefault(c => c.Alias.Equals(world, StringComparison.OrdinalIgnoreCase))
            ?? cartels.FirstOrDefault(c => c.Name.Equals(world, StringComparison.OrdinalIgnoreCase));
        if (match != null) return match;
        if (cartels.Count == 1)
        {
            warning = $"Kein Spielstand '{world}' – nehme den einzigen vorhandenen: '{cartels[0].Name}'. In server.cfg 'World={cartels[0].Alias}' eintragen.";
            return cartels[0];
        }
        warning = cartels.Count == 0
            ? "Keine Spielstände gefunden. Zuerst im Spiel unter 'Neues Kartell' eine Welt anlegen."
            : $"Kein Spielstand '{world}'. Vorhanden: {string.Join(", ", cartels.Select(c => c.Alias))}. In server.cfg bei 'World=' einen davon eintragen.";
        return null;
    }

    // CartelDefaults.sav (GVAS): Property "CartelCustomName" (TextProperty), danach der Text als FString.
    internal static string? ReadCustomName(string file)
    {
        try
        {
            if (!File.Exists(file)) return null;
            var data = File.ReadAllBytes(file);
            var key = Encoding.ASCII.GetBytes("CartelCustomName\0");
            var at = data.AsSpan().IndexOf(key);
            if (at < 0) return null;
            // Im Bereich danach das erste plausible FString (int32 Länge, Text, \0) nach "TextProperty" suchen.
            var start = at + key.Length;
            var typeKey = Encoding.ASCII.GetBytes("TextProperty\0");
            var typeAt = data.AsSpan(start, Math.Min(64, data.Length - start)).IndexOf(typeKey);
            if (typeAt >= 0) start += typeAt + typeKey.Length;
            for (var i = start; i < Math.Min(start + 96, data.Length - 5); i++)
            {
                var len = BitConverter.ToInt32(data, i);
                if (len >= 2 && len <= 128 && i + 4 + len <= data.Length && data[i + 4 + len - 1] == 0)
                {
                    var bytes = data.AsSpan(i + 4, len - 1);
                    var text = Encoding.UTF8.GetString(bytes);
                    // Lokalisierungsschlüssel (32 Hex-Zeichen) überspringen, das ist nicht der Name.
                    var isKey = text.Length == 32 && text.All(Uri.IsHexDigit);
                    if (bytes.ToArray().All(b => b >= 0x20) && !isKey) return text;
                }
                if (len < 0 && len >= -128 && i + 4 - len * 2 <= data.Length)
                {
                    var s = Encoding.Unicode.GetString(data, i + 4, (-len - 1) * 2);
                    if (s.Length > 0 && s.All(ch => !char.IsControl(ch))) return s;
                }
            }
        }
        catch (IOException) { }
        return null;
    }
}
