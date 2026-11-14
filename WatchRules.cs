namespace CrashWatch;

internal enum EventSeverity { Critical, Warning, Info }

internal sealed record WatchRule(
    string Provider,
    int EventId,
    EventSeverity Severity,
    string Title,
    string Detail,
    bool Aggregate,
    int SuppressMinutes,
    string Log = "System",
    string? MessageContains = null);

internal static class WatchRules
{
    private const string WheaProvider = "Microsoft-Windows-WHEA-Logger";
    private const string ProcessorPower = "Microsoft-Windows-Kernel-Processor-Power";

    public static IReadOnlyList<WatchRule> BuildFromConfig(AppConfig cfg)
    {
        var rules = new List<WatchRule>();

        if (cfg.WatchWhea)
        {
            foreach (var id in new[] { 18, 19, 20, 46, 47 })
                rules.Add(new WatchRule(WheaProvider, id, EventSeverity.Critical,
                    "Fatal hardware error",
                    "A fatal hardware error (machine check) was logged. This preceded your last bugcheck.",
                    Aggregate: false, SuppressMinutes: cfg.WheaSuppressionMinutes));
            foreach (var id in new[] { 1, 17 })
                rules.Add(new WatchRule(WheaProvider, id, EventSeverity.Warning,
                    "Corrected hardware error",
                    "A corrected hardware error was logged. Watch for repeats.",
                    Aggregate: false, SuppressMinutes: cfg.WheaSuppressionMinutes));
        }

        if (cfg.WatchStorage)
        {
            rules.Add(new WatchRule("disk", 153, EventSeverity.Warning,
                "Disk I/O retried",
                "The disk timed out and Windows retried the I/O. Repeats point at a cable/port/drive problem.",
                Aggregate: true, SuppressMinutes: 30));
            rules.Add(new WatchRule("disk", 7, EventSeverity.Warning,
                "Bad block detected",
                "The disk reported a bad block.",
                Aggregate: true, SuppressMinutes: 30));
            rules.Add(new WatchRule("disk", 51, EventSeverity.Warning,
                "Paging error on disk",
                "A paging operation hit an error on the disk.",
                Aggregate: true, SuppressMinutes: 30));
            rules.Add(new WatchRule("storahci", 129, EventSeverity.Warning,
                "SATA port reset",
                "The SATA controller reset the port — the drive stopped responding.",
                Aggregate: true, SuppressMinutes: 30));
            rules.Add(new WatchRule("storahci", 130, EventSeverity.Warning,
                "SATA port reset",
                "The SATA controller reset the port — the drive stopped responding.",
                Aggregate: true, SuppressMinutes: 30));
        }

        if (cfg.WatchDumpFailure)
        {
            rules.Add(new WatchRule("volmgr", 161, EventSeverity.Warning,
                "Crash dump write failed",
                "Windows could not write the memory dump during a bugcheck — the evidence is lost.",
                Aggregate: false, SuppressMinutes: 60));
        }

        if (cfg.WatchNtfs)
        {
            rules.Add(new WatchRule("Ntfs", 55, EventSeverity.Warning,
                "NTFS volume error",
                "File system corruption detected on a volume.",
                Aggregate: true, SuppressMinutes: 30));
            rules.Add(new WatchRule("Ntfs", 140, EventSeverity.Warning,
                "NTFS corruption",
                "Windows could not open a file due to file system corruption.",
                Aggregate: true, SuppressMinutes: 30));
        }

        if (cfg.WatchGpu)
        {
            // The shell dying without any TDR/DxgKrnl event is this machine's GPU-crash signature.
            rules.Add(new WatchRule("Microsoft-Windows-Winlogon", 1002, EventSeverity.Critical,
                "Shell (explorer.exe) crashed",
                "The Windows shell stopped unexpectedly and was restarted by userinit. Often follows a GPU driver failure.",
                Aggregate: true, SuppressMinutes: 15, Log: "Application"));
            rules.Add(new WatchRule("Application Error", 1000, EventSeverity.Critical,
                "DWM crashed",
                "Desktop Window Manager (dwm.exe) crashed — the GPU compositor died.",
                Aggregate: true, SuppressMinutes: 30, Log: "Application", MessageContains: "dwm"));
            rules.Add(new WatchRule("Display", 4101, EventSeverity.Critical,
                "GPU driver timeout (TDR)",
                "The display driver stopped responding and was recovered by Windows.",
                Aggregate: true, SuppressMinutes: 15, Log: "System"));
            foreach (var id in new[] { 4101, 4113, 4119, 4121, 4129, 4132, 4133 })
            {
                rules.Add(new WatchRule("Microsoft-Windows-DxgKrnl", id, EventSeverity.Critical,
                    "Graphics kernel error",
                    "The graphics kernel reported an error (GPU failure or reset).",
                    Aggregate: true, SuppressMinutes: 15, Log: "System"));
                rules.Add(new WatchRule("DxgKrnl", id, EventSeverity.Critical,
                    "Graphics kernel error",
                    "The graphics kernel reported an error (GPU failure or reset).",
                    Aggregate: true, SuppressMinutes: 15, Log: "System"));
            }
        }

        if (cfg.WatchThermal)
        {
            rules.Add(new WatchRule(ProcessorPower, 37, EventSeverity.Warning,
                "Processor throttled",
                "The CPU was throttled (thermal or power limit).",
                Aggregate: true, SuppressMinutes: 120));
            rules.Add(new WatchRule(ProcessorPower, 86, EventSeverity.Warning,
                "Thermal event",
                "Thermal throttling was detected.",
                Aggregate: true, SuppressMinutes: 120));
        }

        return rules;
    }

    /// <summary>Builds a single XPath filter covering every (provider, id) pair.</summary>
    public static string BuildXPath(IReadOnlyList<WatchRule> rules)
    {
        var clauses = rules
            .GroupBy(r => r.Provider)
            .Select(g =>
            {
                var ids = string.Join(" or ", g.Select(r => $"EventID={r.EventId}"));
                return $"Provider[@Name='{g.Key}'] and ({ids})";
            });

        return $"*[System[{string.Join(" or ", clauses)}]]";
    }
}
