using System.Diagnostics;

namespace CrashWatch;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var testToast = args.Contains("--test-toast", StringComparer.OrdinalIgnoreCase);
        var testSweep = args.Contains("--test-sweep", StringComparer.OrdinalIgnoreCase);

        using var mutex = new Mutex(true, @"Global\CrashWatch", out bool createdNew);
        if (!createdNew) return 0;

        AppConfig.Load();
        if (AppConfig.Current.PriorityHigh)
        {
            try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High; } catch { }
        }

        if (testToast)
        {
            ToastService.Show(
                "CrashWatch test toast",
                "If you can read this, toast notifications work from this context.",
                critical: false);
            Thread.Sleep(2000);
            return 0;
        }

        if (testSweep)
        {
            BootSweep.Run(toast: true, history: null);
            Thread.Sleep(2000);
            return 0;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayContext());
        return 0;
    }
}
