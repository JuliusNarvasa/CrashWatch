namespace CrashWatch;

/// <summary>
/// Watches C:\Windows\LiveKernelReports for new .dmp files — these are written when the
/// display driver resets (TDR) or another live kernel report occurs, even when no bugcheck happens.
/// </summary>
internal sealed class GpuDumpWatcher : IDisposable
{
    private FileSystemWatcher? _fsw;
    private readonly string _dir = @"C:\Windows\LiveKernelReports";
    private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public event Action<string>? DumpCreated;

    public void Start()
    {
        try
        {
            Directory.CreateDirectory(_dir); // no-op if it exists; needs elevation

            // Baseline: never toast for dumps that already existed.
            foreach (var f in Directory.EnumerateFiles(_dir, "*.dmp", SearchOption.AllDirectories))
                _seen.Add(f);

            _fsw = new FileSystemWatcher(_dir, "*.dmp")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.LastWrite,
            };
            _fsw.Created += (_, e) => OnDump(e.FullPath);
            _fsw.EnableRaisingEvents = true;
            Log.Write($"LiveKernelReports watcher active (baseline {_seen.Count} dump(s))");
        }
        catch (Exception ex)
        {
            Log.Write("LiveKernelReports watcher failed: " + ex.Message);
        }
    }

    private void OnDump(string path)
    {
        lock (_lock)
        {
            if (!_seen.Add(path)) return;
        }
        Log.Write($"live kernel dump detected: {path}");
        DumpCreated?.Invoke(path);
    }

    public void Dispose()
    {
        if (_fsw == null) return;
        try
        {
            _fsw.EnableRaisingEvents = false;
            _fsw.Dispose();
        }
        catch { }
        _fsw = null;
    }
}
