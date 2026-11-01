namespace DDS2Mod.Shared;

/// <summary>Sichert die Spielstände vor jedem Start mit Mod-Einstellungen.</summary>
public static class SaveBackup
{
    const int Keep = 10;

    public static string? Create()
    {
        var source = Path.Combine(GameLocator.SavedDir, "SaveGames");
        if (!Directory.Exists(source)) return null;

        var root = Path.Combine(GameLocator.SavedDir, "DDS2Mod-Backups");
        var target = Path.Combine(root, DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        CopyDirectory(source, target);

        foreach (var old in Directory.GetDirectories(root).OrderByDescending(d => d).Skip(Keep))
            Directory.Delete(old, recursive: true);
        return target;
    }

    static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from))
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)), overwrite: true);
        foreach (var dir in Directory.GetDirectories(from))
            CopyDirectory(dir, Path.Combine(to, Path.GetFileName(dir)));
    }
}
