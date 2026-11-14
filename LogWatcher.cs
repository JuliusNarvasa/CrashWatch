using System.Diagnostics.Eventing.Reader;

namespace CrashWatch;

/// <summary>
/// Push-based watcher over the Windows event logs (System + Application).
/// Fires <see cref="RuleFired"/> whenever an event matching one of the rules arrives.
/// </summary>
internal sealed class LogWatcher : IDisposable
{
    private readonly List<EventLogWatcher> _watchers = new();
    private List<WatchRule> _rules = new();

    public event Action<WatchRule, EventRecord>? RuleFired;

    public void Start()
    {
        Stop();
        _rules = WatchRules.BuildFromConfig(AppConfig.Current).ToList();
        if (_rules.Count == 0)
        {
            Log.Write("watcher: no rules enabled");
            return;
        }

        foreach (var group in _rules.GroupBy(r => r.Log))
        {
            try
            {
                var xpath = WatchRules.BuildXPath(group.ToList());
                var query = new EventLogQuery(group.Key, PathType.LogName, xpath);
                var watcher = new EventLogWatcher(query);
                watcher.EventRecordWritten += OnEvent;
                watcher.Enabled = true;
                _watchers.Add(watcher);
                Log.Write($"watcher [{group.Key}] started with {group.Count()} rules");
            }
            catch (Exception ex)
            {
                Log.Write($"watcher [{group.Key}] failed to start: {ex.Message}");
            }
        }
    }

    private void OnEvent(object? sender, EventRecordWrittenEventArgs e)
    {
        try
        {
            if (e.EventRecord is not { } rec) return;
            var rule = _rules.FirstOrDefault(r =>
                string.Equals(r.Provider, rec.ProviderName, StringComparison.OrdinalIgnoreCase) &&
                r.EventId == rec.Id);
            if (rule == null) return;

            if (rule.MessageContains != null)
            {
                var msg = rec.FormatDescription() ?? "";
                if (!msg.Contains(rule.MessageContains, StringComparison.OrdinalIgnoreCase)) return;
            }

            RuleFired?.Invoke(rule, rec);
        }
        catch (Exception ex)
        {
            Log.Write("watcher event error: " + ex.Message);
        }
    }

    public void Stop()
    {
        foreach (var w in _watchers)
        {
            try
            {
                w.Enabled = false;
                w.Dispose();
            }
            catch { }
        }
        _watchers.Clear();
    }

    public void Dispose() => Stop();
}
