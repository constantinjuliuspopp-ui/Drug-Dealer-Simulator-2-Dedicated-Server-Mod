using DDS2Mod.Client;
using DDS2Mod.Server;
using DDS2Mod.Shared;

namespace DDS2Mod;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        if (HasFlag(args, "--server"))
            return ServerMode.Run(args);

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
