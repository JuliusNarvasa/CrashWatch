namespace CrashWatch;

/// <summary>
/// Per-(provider, event id) toast suppression and burst aggregation.
/// </summary>
internal sealed class RateLimiter
{
    private readonly object _lock = new();
    private readonly Dictionary<(string Provider, int Id), Queue<DateTime>> _windows = new();
    private readonly Dictionary<(string Provider, int Id), DateTime> _lastToast = new();

    public bool ShouldToast(WatchRule rule, AppConfig cfg, out int burstCount)
    {
        lock (_lock)
        {
            var key = (rule.Provider, rule.EventId);
            var now = DateTime.UtcNow;

            if (!_windows.TryGetValue(key, out var window))
                _windows[key] = window = new Queue<DateTime>();
            window.Enqueue(now);

            var windowMinutes = Math.Max(
                rule.Aggregate ? cfg.StorageAggregateMinutes : rule.SuppressMinutes,
                rule.SuppressMinutes);
            while (window.Count > 0 && (now - window.Peek()).TotalMinutes > windowMinutes)
                window.Dequeue();

            burstCount = window.Count;

            if (_lastToast.TryGetValue(key, out var last) &&
                (now - last).TotalMinutes < rule.SuppressMinutes)
                return false;

            if (rule.Aggregate && window.Count < cfg.StorageAggregateThreshold)
                return false;

            _lastToast[key] = now;
            return true;
        }
    }
}
