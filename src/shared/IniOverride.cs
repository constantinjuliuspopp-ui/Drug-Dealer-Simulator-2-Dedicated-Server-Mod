using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace DDS2Mod.Shared;

public sealed record OverrideSettings(int Port, int MaxPlayers, string Password);

/// <summary>
/// Schreibt die Mod-Einstellungen in die BENUTZER-Configs des Spiels
/// (%LOCALAPPDATA%\DrugDealerSimulator2\Saved\Config\Windows). Spieldateien bleiben unberührt.
/// Vor der ersten Änderung wird ein Backup angelegt; RestoreAll stellt es wieder her.
/// </summary>
public static class IniOverride
{
    const string BlockStart = "; >>> DDS2Mod (nicht von Hand bearbeiten)";
    const string BlockEnd = "; <<< DDS2Mod";
    static readonly string[] Files = { "Engine.ini", "Game.ini" };

    /// <summary>
    /// Fließt in die Netzwerk-Version der Engine ein: ein abweichendes Passwort führt zur Ablehnung
    /// "inkompatible Version". Nur mit Passwort gesetzt – ohne Passwort bleibt die Netzwerk-Version
    /// die des Spiels, damit auch Spieler ohne Launcher (normaler Start über Steam) beitreten können.
    /// </summary>
    public static string NetworkVersionTag(string password) =>
        $"DDS2Mod-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(password)))[..8].ToLowerInvariant()}";

    /// <summary>
    /// Der Netzwerk-Treiber wird hier bewusst NICHT umgestellt: Das Spiel hostet über den SteamNetDriver, und
    /// Direkt-IP läuft über dessen Passthrough (siehe DDS2Net main.lua, ?bIsLanMatch). Ein IpNetDriver-Eintrag
    /// in der Engine.ini kam im Spiel ohnehin nicht an.
    /// </summary>
    public static void Apply(OverrideSettings s, string? configDir = null)
    {
        configDir ??= GameLocator.ConfigDir;
        Directory.CreateDirectory(configDir);
        ApplyFile(Path.Combine(configDir, "Engine.ini"), $"""
            {BlockStart}
            [URL]
            Port={s.Port}
            {BlockEnd}
            """);
        var version = string.IsNullOrEmpty(s.Password) ? "" : $"""
            [/Script/EngineSettings.GeneralProjectSettings]
            ProjectVersion={NetworkVersionTag(s.Password)}

            """;
        ApplyFile(Path.Combine(configDir, "Game.ini"), $"""
            {BlockStart}
            {version}[/Script/Engine.GameSession]
            MaxPlayers={s.MaxPlayers}
            {BlockEnd}
            """);
    }

    public static bool IsApplied(string? configDir = null)
    {
        configDir ??= GameLocator.ConfigDir;
        return Files.Any(f => File.Exists(Path.Combine(configDir, f) + ".dds2mod-backup")
                           || File.Exists(Path.Combine(configDir, f) + ".dds2mod-nofile"));
    }

    public static void RestoreAll(string? configDir = null)
    {
        configDir ??= GameLocator.ConfigDir;
        foreach (var f in Files) Restore(Path.Combine(configDir, f));
    }

    static void ApplyFile(string ini, string block)
    {
        var backup = ini + ".dds2mod-backup";
        var noFile = ini + ".dds2mod-nofile";
        // Backup nur einmal: ein zweites Apply darf das Original nicht überschreiben.
        if (!File.Exists(backup) && !File.Exists(noFile))
        {
            if (File.Exists(ini)) File.Copy(ini, backup);
            else File.WriteAllText(noFile, "");
        }
        var rest = File.Exists(ini) ? StripBlock(File.ReadAllText(ini)) : "";
        var content = (rest.Length > 0 ? rest + "\r\n\r\n" : "") + block.ReplaceLineEndings("\r\n") + "\r\n";
        File.WriteAllText(ini, content, new UTF8Encoding(false));
    }

    static void Restore(string ini)
    {
        var backup = ini + ".dds2mod-backup";
        var noFile = ini + ".dds2mod-nofile";
        if (File.Exists(backup))
        {
            File.Copy(backup, ini, overwrite: true);
            File.Delete(backup);
        }
        else if (File.Exists(noFile))
        {
            File.Delete(ini);
            File.Delete(noFile);
        }
        else if (File.Exists(ini))
        {
            var text = File.ReadAllText(ini);
            if (text.Contains(BlockStart)) File.WriteAllText(ini, StripBlock(text) + "\r\n", new UTF8Encoding(false));
        }
    }

    internal static string StripBlock(string text) =>
        Regex.Replace(text, @"(?s)\r?\n?" + Regex.Escape(BlockStart) + ".*?" + Regex.Escape(BlockEnd) + @"\r?\n?", "\r\n").TrimEnd();
}
