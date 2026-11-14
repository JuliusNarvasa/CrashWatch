using System.Diagnostics.Eventing.Reader;
using System.Text.Json;
using System.Xml.Linq;

namespace CrashWatch;

internal static class EventXml
{
    /// <summary>Parses the named EventData fields out of an event record.</summary>
    public static Dictionary<string, string> ParseEventData(EventRecord rec)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var x = XElement.Parse(rec.ToXml());
            foreach (var el in x.Descendants().Where(e => e.Name.LocalName == "Data"))
            {
                var name = el.Attribute("Name")?.Value;
                if (!string.IsNullOrEmpty(name) && !result.ContainsKey(name))
                    result[name] = el.Value;
            }
        }
        catch
        {
            // fall back to empty
        }
        return result;
    }
}

internal sealed class BootState
{
    public DateTime? LastSeenShutdownUtc { get; set; }
    public DateTime? LastEventSweepUtc { get; set; }

    private static string FilePath => Path.Combine(Log.Dir, "state.json");
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static BootState Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<BootState>(File.ReadAllText(FilePath), JsonOpts) ?? new BootState();
        }
        catch { }
        return new BootState();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Log.Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOpts));
        }
        catch (Exception ex)
        {
            Log.Write("state save failed: " + ex.Message);
        }
    }
}

/// <summary>
/// On startup, checks whether the previous shutdown was a crash (bugcheck / dirty shutdown)
/// and reports it. Also used by the "Check now" tray command.
/// </summary>
internal static class BootSweep
{
    public sealed record Report(
        DateTime RebootTime,
        int BugcheckCode,
        ulong P1, ulong P2, ulong P3, ulong P4,
        string? DumpPath)
    {
        public bool HadBugcheck => BugcheckCode != 0;
    }

    private const string KernelPower = "Microsoft-Windows-Kernel-Power";
    private const string EventLogProvider = "EventLog";
    private const string WerProvider = "Microsoft-Windows-WER-SystemErrorReporting";

    /// <summary>Returns reports for crashes not yet reported. Optionally toasts and records them.</summary>
    public static List<Report> Run(bool toast, HistoryStore? history)
    {
        var reports = new List<Report>();
        try
        {
            var state = BootState.Load();
            var cfg = AppConfig.Current;
            var lookback = DateTime.UtcNow.AddHours(-cfg.LookbackHoursForBootSweep);

            var bc = ReadLastBugcheck();
            var dirty = ReadLastDirtyShutdown();
            var dump = ReadLastDumpInfo();

            DateTime? evtTime = null;
            if (bc?.Time is DateTime t) evtTime = t;
            if (dirty?.Time is DateTime d && (evtTime == null || d > evtTime)) evtTime = d;

            if (evtTime.HasValue &&
                evtTime.Value >= lookback &&
                (state.LastSeenShutdownUtc == null || evtTime.Value > state.LastSeenShutdownUtc.Value))
            {
                var report = new Report(
                    evtTime.Value,
                    bc?.BugcheckCode ?? 0,
                    bc?.P1 ?? 0, bc?.P2 ?? 0, bc?.P3 ?? 0, bc?.P4 ?? 0,
                    dump);

                reports.Add(report);
                state.LastSeenShutdownUtc = evtTime;
                state.Save();

                if (history != null)
                {
                    var (name, detail) = BugcheckDecoder.Decode(report.BugcheckCode, report.P1, report.P2, report.P3, report.P4);
                    history.Add(report.RebootTime, report.HadBugcheck ? "Critical" : "Warning",
                        "Kernel-Power", 41,
                        report.HadBugcheck
                            ? $"0x{report.BugcheckCode:X} {name} — {detail}"
                            : $"{name} — {detail}");
                }

                if (toast)
                    ToastReport(report);
            }

            SweepMissedGpuEvents(state, history, toast);
        }
        catch (Exception ex)
        {
            Log.Write("boot sweep failed: " + ex.Message);
        }
        return reports;
    }

    /// <summary>
    /// Reports GPU-related events (shell crashes, DWM crashes) that landed while the watcher
    /// was not running, so a GPU crash that kills the tray app is still reported next start.
    /// </summary>
    private static void SweepMissedGpuEvents(BootState state, HistoryStore? history, bool toast)
    {
        try
        {
            if (!AppConfig.Current.WatchGpu) return;

            var since = state.LastEventSweepUtc
                        ?? DateTime.UtcNow.AddHours(-AppConfig.Current.LookbackHoursForBootSweep);
            var until = DateTime.UtcNow;
            state.LastEventSweepUtc = until;
            state.Save();

            var iso = since.ToUniversalTime().ToString("o");
            var xpath = $"*[System[TimeCreated[@SystemTime >= '{iso}'] and " +
                        "((Provider[@Name='Microsoft-Windows-Winlogon'] and (EventID=1002)) or " +
                        "(Provider[@Name='Application Error'] and (EventID=1000)))]]";

            using var reader = new EventLogReader(new EventLogQuery("Application", PathType.LogName, xpath));
            int shells = 0, dwms = 0;
            EventRecord? rec;
            while ((rec = reader.ReadEvent()) != null)
            {
                using (rec)
                {
                    var msg = rec.FormatDescription() ?? "";
                    if (string.Equals(rec.ProviderName, "Microsoft-Windows-Winlogon", StringComparison.OrdinalIgnoreCase))
                        shells++;
                    else if (msg.Contains("dwm", StringComparison.OrdinalIgnoreCase))
                        dwms++;
                }
            }

            if (shells + dwms == 0) return;

            var summary = $"While the watcher was down: {shells} shell crash(es), {dwms} DWM crash(es).";
            Log.Write(summary);
            history?.Add(until, "Warning", "BootSweep/GPU", 0, summary);
            if (toast)
                ToastService.Show("GPU-related crashes missed", summary, critical: true);
        }
        catch (Exception ex)
        {
            Log.Write("gpu sweep failed: " + ex.Message);
        }
    }

    private static void ToastReport(Report r)
    {
        var (name, detail) = BugcheckDecoder.Decode(r.BugcheckCode, r.P1, r.P2, r.P3, r.P4);

        string title = r.HadBugcheck
            ? $"PC crashed — 0x{r.BugcheckCode:X} {name}"
            : $"PC crashed — {name}";

        var body = $"{detail}\nRebooted at {r.RebootTime.ToLocalTime():HH:mm}.";
        if (!string.IsNullOrEmpty(r.DumpPath))
            body += $"\nDump saved: {r.DumpPath}";

        ToastService.Show(title, body, critical: true);
    }

    private static (DateTime Time, int BugcheckCode, ulong P1, ulong P2, ulong P3, ulong P4)? ReadLastBugcheck()
    {
        var rec = ReadNewest($"*[System[Provider[@Name='{KernelPower}'] and (EventID=41)]]");
        if (rec == null) return null;

        var data = EventXml.ParseEventData(rec);
        int.TryParse(data.TryGetValue("BugcheckCode", out var c) ? c : "0", out int code);
        ulong p1 = ParseHex(data, "BugcheckParameter1");
        ulong p2 = ParseHex(data, "BugcheckParameter2");
        ulong p3 = ParseHex(data, "BugcheckParameter3");
        ulong p4 = ParseHex(data, "BugcheckParameter4");
        return (rec.TimeCreated ?? DateTime.UtcNow, code, p1, p2, p3, p4);
    }

    private static (DateTime Time, bool Dirty)? ReadLastDirtyShutdown()
    {
        var rec = ReadNewest($"*[System[Provider[@Name='{EventLogProvider}'] and (EventID=6008)]]");
        return rec?.TimeCreated is DateTime t ? (t, true) : null;
    }

    private static string? ReadLastDumpInfo()
    {
        var rec = ReadNewest($"*[System[Provider[@Name='{WerProvider}'] and (EventID=1001)]]");
        if (rec == null) return null;
        var msg = rec.FormatDescription() ?? "";
        var m = System.Text.RegularExpressions.Regex.Match(
            msg, @"A dump was saved in:\s*(\S+)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }

    private static ulong ParseHex(Dictionary<string, string> data, string key)
    {
        if (!data.TryGetValue(key, out var s)) return 0;
        s = s.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s[2..];
        return ulong.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out var v) ? v : 0;
    }

    private static EventRecord? ReadNewest(string xpath)
    {
        var query = new EventLogQuery("System", PathType.LogName, xpath) { ReverseDirection = true };
        using var reader = new EventLogReader(query);
        return reader.ReadEvent();
    }
}
