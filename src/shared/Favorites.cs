using System.Text.Json;

namespace DDS2Mod.Shared;

/// <param name="QueryPort">Abfrage-Port des Servers, 0 = Port + 1.</param>
public sealed record Favorite(string Name, string Host, int Port, int QueryPort = 0)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public int EffectiveQueryPort => QueryPort > 0 ? QueryPort : ServerQuery.DefaultQueryPort(Port);
    public override string ToString() => $"{Name}  ({Host}:{Port})";
}

/// <summary>Favoritenliste in %APPDATA%\DDS2Mod\favorites.json (Format wie src/shared/favorites.example.json).</summary>
public static class Favorites
{
    sealed record FileModel(int Version, List<Favorite> Favorites);

    static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault,
    };

    static string FilePath => Path.Combine(ModInfo.AppDataDir, "favorites.json");

    public static List<Favorite> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new();
            return JsonSerializer.Deserialize<FileModel>(File.ReadAllText(FilePath), Options)?.Favorites ?? new();
        }
        catch (JsonException)
        {
            // Kaputte Datei nicht überschreiben, sondern beiseitelegen.
            File.Move(FilePath, FilePath + ".defekt", overwrite: true);
            return new();
        }
    }

    public static void Save(List<Favorite> favorites)
    {
        Directory.CreateDirectory(ModInfo.AppDataDir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(new FileModel(1, favorites), Options));
        ExportForGame(favorites);
    }

    /// <summary>
    /// favorites.txt für das Menü im Spiel (Lua hat keinen JSON-Parser):
    /// eine Zeile "Name|Host|Port|Ping|Spieler|Max|Zustand" (Ping/Spieler leer = unbekannt).
    /// </summary>
    public static void ExportForGame(List<Favorite> favorites, IReadOnlyDictionary<Favorite, ServerInfo?>? infos = null, string? dir = null)
    {
        dir ??= ModInfo.AppDataDir;
        Directory.CreateDirectory(dir);
        var lines = favorites.Select(f =>
        {
            ServerInfo? info = null;
            var queried = infos != null && infos.TryGetValue(f, out info);
            return string.Join('|', f.Name.Replace('|', '/'), f.Host, f.Port,
                info is null ? "" : info.Ping, info is null ? "" : info.Players, info is null ? "" : info.MaxPlayers,
                JoinRequests.StateCode(info, queried));
        });
        var path = Path.Combine(dir, "favorites.txt");
        File.WriteAllLines(path + ".tmp", lines, new System.Text.UTF8Encoding(false));
        File.Move(path + ".tmp", path, overwrite: true);
    }
}
