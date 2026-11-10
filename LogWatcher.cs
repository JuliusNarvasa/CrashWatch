using System.Diagnostics.Eventing.Reader;

namespace CrashWatch;

/// <summary>
/// Push-based watcher over the System event log. Fires <see cref="RuleFired"/> whenever
/// an event matching one of the rules arrives.
/// </summary>
internal sealed class LogWatcher : IDisposable
{
    private EventLogWatcher? _watcher;
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

        var xpath = WatchRules.BuildXPath(_rules);
        Log.Write($"watcher started with {_rules.Count} rules");

        var query = new EventLogQuery("System", PathType.LogName, xpath);
        _watcher = new EventLogWatcher(query);
        _watcher.EventRecordWritten += OnEvent;
        _watcher.Enabled = true;
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
            RuleFired?.Invoke(rule, rec);
        }
        catch (Exception ex)
        {
            Log.Write("watcher event error: " + ex.Message);
        }
    }

    public void Stop()
    {
        if (_watcher == null) return;
        try
        {
            _watcher.Enabled = false;
            _watcher.Dispose();
        }
        catch { }
        _watcher = null;
    }

    public void Dispose() => Stop();
}
