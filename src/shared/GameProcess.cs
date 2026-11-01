using System.Diagnostics;

namespace DDS2Mod.Shared;

/// <summary>Startet die Spielinstanz mit Rolle und IPC-Ordner für die DDS2Net-Mod.</summary>
public static class GameProcess
{
    public static Process Launch(string gameDir, string role, string ipcDir, IEnumerable<string> gameArgs,
        IReadOnlyDictionary<string, string>? extraEnv = null)
    {
        var psi = new ProcessStartInfo(GameLocator.ShippingExe(gameDir))
        {
            UseShellExecute = false,
            WorkingDirectory = GameLocator.Win64Dir(gameDir),
        };
        foreach (var a in gameArgs) psi.ArgumentList.Add(a);
        psi.Environment["DDS2_ROLE"] = role;
        psi.Environment["DDS2_IPC_DIR"] = ipcDir;
        psi.Environment["DDS2_DEFAULT_PORT"] = ModInfo.DefaultPort.ToString();
        foreach (var (k, v) in extraEnv ?? new Dictionary<string, string>()) psi.Environment[k] = v;
        return Process.Start(psi) ?? throw new InvalidOperationException("Spiel konnte nicht gestartet werden");
    }

    /// <summary>Neueste Änderung im Spielstand-Ordner (zum Prüfen, ob gespeichert wurde).</summary>
    public static (string File, DateTime Time)? NewestSaveFile()
    {
        var dir = Path.Combine(GameLocator.SavedDir, "SaveGames");
        if (!Directory.Exists(dir)) return null;
        var newest = new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories)
            .OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault();
        return newest is null ? null : (newest.Name, newest.LastWriteTimeUtc);
    }

    /// <summary>Normaler Start über Steam (ohne Mod-Einstellungen), z. B. für Steam-Koop.</summary>
    public static void LaunchViaSteam() =>
        Process.Start(new ProcessStartInfo($"steam://rungameid/{ModInfo.SteamAppId}") { UseShellExecute = true });

    public static bool IsModInstalled(string gameDir) =>
        File.Exists(Path.Combine(GameLocator.Win64Dir(gameDir), "ue4ss", "Mods", "DDS2Net", "Scripts", "main.lua"));
}
