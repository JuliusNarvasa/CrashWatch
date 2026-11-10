using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrashWatch;

internal sealed class AppConfig
{
    public int LookbackHoursForBootSweep { get; set; } = 24;
    public bool WatchWhea { get; set; } = true;
    public bool WatchStorage { get; set; } = true;
    public bool WatchThermal { get; set; } = true;
    public bool WatchDumpFailure { get; set; } = true;
    public bool WatchNtfs { get; set; } = true;
    public int DefaultSuppressionMinutes { get; set; } = 15;
    public int StorageAggregateMinutes { get; set; } = 10;
    public int StorageAggregateThreshold { get; set; } = 3;
    public int WheaSuppressionMinutes { get; set; } = 60;
    public int WheaEscalationCount24h { get; set; } = 2;
    public bool PriorityHigh { get; set; } = true;
    public DateTime? PauseUntilUtc { get; set; }

    [JsonIgnore]
    public bool IsPaused => PauseUntilUtc.HasValue && PauseUntilUtc.Value > DateTime.UtcNow;

    public static AppConfig Current { get; private set; } = new();

    public static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CrashWatch");

    public static string FilePath => Path.Combine(Dir, "config.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static void Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var cfg = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(FilePath), JsonOpts);
                if (cfg != null) Current = cfg;
            }
        }
        catch (Exception ex)
        {
            Log.Write("config load failed, using defaults: " + ex.Message);
            Current = new AppConfig();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOpts));
        }
        catch (Exception ex)
        {
            Log.Write("config save failed: " + ex.Message);
        }
    }

    /// <summary>Reloads the config whenever the file changes on disk (live-editable).</summary>
    public static void StartWatching(Action onChanged)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var fsw = new FileSystemWatcher(Dir, "config.json")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size
            };
            var debounce = DateTime.MinValue;
            fsw.Changed += (_, _) =>
            {
                // editors fire multiple events; debounce 500 ms
                if ((DateTime.UtcNow - debounce).TotalMilliseconds < 500) return;
                debounce = DateTime.UtcNow;
                Thread.Sleep(200);
                var old = Current;
                Load();
                if (!ReferenceEquals(old, Current))
                {
                    Log.Write("config.json reloaded");
                    onChanged();
                }
            };
            fsw.EnableRaisingEvents = true;
        }
        catch (Exception ex)
        {
            Log.Write("config watcher failed: " + ex.Message);
        }
    }
}
