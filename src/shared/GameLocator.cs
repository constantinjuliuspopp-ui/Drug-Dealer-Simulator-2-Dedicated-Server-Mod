using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace DDS2Mod.Shared;

/// <summary>Findet das Spiel über die Steam-Registry und libraryfolders.vdf.</summary>
public static class GameLocator
{
    public const string ShippingProcessName = "DrugDealerSimulator2-Win64-Shipping";

    public static string SavedDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DrugDealerSimulator2", "Saved");

    public static string ConfigDir => Path.Combine(SavedDir, "Config", "Windows");

    public static string Win64Dir(string gameDir) => Path.Combine(gameDir, "DrugDealerSimulator2", "Binaries", "Win64");

    public static string ShippingExe(string gameDir) => Path.Combine(Win64Dir(gameDir), ShippingProcessName + ".exe");

    public static bool IsGameRunning() => Process.GetProcessesByName(ShippingProcessName).Length > 0;

    /// <summary>Spielordner neben der installierten DDS2Mod.exe (vom Installer gemerkt) oder per Steam-Suche.</summary>
    public static string? FindGameDir()
    {
        var remembered = Path.Combine(AppContext.BaseDirectory, "gamedir.txt");
        if (File.Exists(remembered))
        {
            var dir = File.ReadAllText(remembered).Trim();
            if (IsGameDir(dir)) return dir;
        }

        foreach (var lib in SteamLibraries())
        {
            var manifest = Path.Combine(lib, "steamapps", $"appmanifest_{ModInfo.SteamAppId}.acf");
            if (!File.Exists(manifest)) continue;
            var installDir = "Drug Dealer Simulator 2";
            var m = Regex.Match(File.ReadAllText(manifest), "\"installdir\"\\s+\"([^\"]+)\"");
            if (m.Success) installDir = m.Groups[1].Value;
            var dir = Path.Combine(lib, "steamapps", "common", installDir);
            if (IsGameDir(dir)) return dir;
        }
        return null;
    }

    /// <summary>Steam-Build-ID aus appmanifest_1708850.acf (liegt zwei Ebenen über dem Spielordner), "" wenn unbekannt.</summary>
    public static string ReadBuildId(string gameDir)
    {
        try
        {
            var manifest = Path.Combine(gameDir, "..", "..", $"appmanifest_{ModInfo.SteamAppId}.acf");
            var m = Regex.Match(File.ReadAllText(manifest), "\"buildid\"\\s+\"(\\d+)\"");
            return m.Success ? m.Groups[1].Value : "";
        }
        catch (IOException) { return ""; }
        catch (UnauthorizedAccessException) { return ""; }
    }

    /// <summary>Das Spiel startet nur mit laufendem, angemeldetem Steam-Client (Kopierschutz des Spiels).</summary>
    public static bool IsSteamRunning() => Process.GetProcessesByName("steam").Length > 0;

    public static bool IsGameDir(string dir) =>
        !string.IsNullOrWhiteSpace(dir) && File.Exists(Path.Combine(dir, "DrugDealerSimulator2.exe"));

    static IEnumerable<string> SteamLibraries()
    {
        var roots = new List<string>();
        if (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) is string userPath)
            roots.Add(userPath);
        if (Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) is string machinePath)
            roots.Add(machinePath);

        var libs = new List<string>();
        foreach (var root in roots.Select(r => r.Replace('/', '\\')).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            libs.Add(root);
            var vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf)) continue;
            foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                libs.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
        }
        return libs.Select(l => l.TrimEnd('\\')).Distinct(StringComparer.OrdinalIgnoreCase);
    }
}
