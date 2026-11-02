using System.Text.Json;

namespace DDS2Mod.Shared;

/// <summary>Einstellungen des Launchers in %APPDATA%\DDS2Mod\launcher.json (derzeit nur das Relay).</summary>
public sealed class LauncherSettings
{
    public string RelayAddress { get; set; } = "";
    public string RelaySecret { get; set; } = "";
    public bool UseRelay { get; set; }

    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string DefaultPath => Path.Combine(ModInfo.AppDataDir, "launcher.json");

    public static LauncherSettings Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(path), Options) ?? new() : new();
        }
        catch (JsonException) { return new(); }
        catch (IOException) { return new(); }
    }

    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, Options));
    }
}
