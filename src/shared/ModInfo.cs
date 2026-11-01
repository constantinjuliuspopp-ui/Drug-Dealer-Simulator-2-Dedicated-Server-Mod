using System.Text.Json;

namespace DDS2Mod.Shared;

/// <summary>Versionsdaten aus src/shared/version.json (eingebettet).</summary>
public static class ModInfo
{
    public const int SteamAppId = 1708850;

    public static string Version { get; }
    public static int ProtocolVersion { get; }
    public static int DefaultPort { get; }

    public static string AppDataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DDS2Mod");

    static ModInfo()
    {
        using var stream = typeof(ModInfo).Assembly.GetManifestResourceStream("DDS2Mod.version.json")
            ?? throw new InvalidOperationException("version.json fehlt in der Assembly");
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;
        Version = root.GetProperty("modVersion").GetString()!;
        ProtocolVersion = root.GetProperty("protocolVersion").GetInt32();
        DefaultPort = root.GetProperty("defaultPort").GetInt32();
    }
}
