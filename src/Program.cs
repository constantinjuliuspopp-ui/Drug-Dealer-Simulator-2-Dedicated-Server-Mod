using System.Diagnostics;
using DDS2Mod.Client;
using DDS2Mod.Shared;

namespace DDS2Mod;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        // Alte Verknüpfungen (bis 0.2) riefen "DDS2Mod.exe --server" auf: an DDS2Server.exe weiterreichen.
        if (HasFlag(args, "--server"))
        {
            var psi = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "DDS2Server.exe")) { UseShellExecute = true };
            foreach (var a in args.Where(a => !a.Equals("--server", StringComparison.OrdinalIgnoreCase))) psi.ArgumentList.Add(a);
            Process.Start(psi);
            return 0;
        }

        // Unsichtbarer Helfer beim normalen Spielstart: die Mod öffnet "dds2mod://agent" (siehe AgentMode).
        if (HasFlag(args, "--agent") || args.Any(a => a.StartsWith("dds2mod:", StringComparison.OrdinalIgnoreCase)))
            return AgentMode.Run();

        // Wird vom Deinstallationsprogramm aufgerufen.
        if (HasFlag(args, "--restore"))
        {
            IniOverride.RestoreAll();
            return 0;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new ClientForm());
        return 0;
    }

    static bool HasFlag(string[] args, string flag) =>
        args.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));
}
