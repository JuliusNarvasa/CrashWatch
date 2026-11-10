namespace CrashWatch;

internal sealed class HistoryEntry
{
    public DateTime Time { get; set; }
    public string Severity { get; set; } = "Info";
    public string Source { get; set; } = "";
    public int EventId { get; set; }
    public string Message { get; set; } = "";
}

/// <summary>In-memory history plus an append-only CSV on disk.</summary>
internal sealed class HistoryStore
{
    private readonly object _lock = new();
    private static readonly object _fileLock = new();
    private readonly List<HistoryEntry> _entries = new();
    private const int MaxInMemory = 500;

    public event Action<HistoryEntry>? Added;
    public event Action? Cleared;

    public static string Dir => Log.Dir;
    public static string FilePath => Path.Combine(Dir, "history.csv");

    public IReadOnlyList<HistoryEntry> Entries
    {
        get { lock (_lock) return _entries.ToList(); }
    }

    public void Add(DateTime time, string severity, string source, int eventId, string message)
    {
        var entry = new HistoryEntry
        {
            Time = time,
            Severity = severity,
            Source = source,
            EventId = eventId,
            Message = message,
        };

        lock (_lock)
        {
            _entries.Add(entry);
            if (_entries.Count > MaxInMemory)
                _entries.RemoveRange(0, _entries.Count - MaxInMemory);
        }

        AppendCsv(entry);
        Added?.Invoke(entry);
    }

    /// <summary>Wipes the in-memory list and rewrites history.csv (header only).</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _entries.Clear();
        }
        try
        {
            Directory.CreateDirectory(Dir);
            lock (_fileLock)
            {
                File.WriteAllText(FilePath, "time,severity,source,eventid,message" + Environment.NewLine);
            }
        }
        catch (Exception ex)
        {
            Log.Write("history clear failed: " + ex.Message);
        }
        Log.Write("history cleared by user");
        Cleared?.Invoke();
    }

    /// <summary>Counts matching entries within the last 24 h (for escalation wording).</summary>
    public int CountInLast24h(string source, params int[] eventIds)
    {
        lock (_lock)
        {
            var cutoff = DateTime.UtcNow.AddHours(-24);
            return _entries.Count(e =>
                e.Source == source && eventIds.Contains(e.EventId) && e.Time >= cutoff);
        }
    }

    private static void AppendCsv(HistoryEntry e)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var line = string.Join(",",
                Csv(e.Time.ToString("yyyy-MM-dd HH:mm:ss")),
                Csv(e.Severity),
                Csv(e.Source),
                e.EventId.ToString(),
                Csv(e.Message));
            lock (_fileLock)
            {
                File.AppendAllText(FilePath, line + Environment.NewLine);
            }
        }
        catch (Exception ex)
        {
            Log.Write("history append failed: " + ex.Message);
        }
    }

    private static string Csv(string? s)
    {
        s ??= "";
        if (s.Contains('"') || s.Contains(',') || s.Contains('\n'))
            s = "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }
}
