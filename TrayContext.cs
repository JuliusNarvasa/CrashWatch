using System.Diagnostics;

namespace CrashWatch;

internal sealed class TrayContext : ApplicationContext
{
    private readonly NotifyIcon _icon;
    private readonly HistoryStore _history = new();
    private readonly RateLimiter _limiter = new();
    private readonly LogWatcher _watcher = new();
    private readonly GpuDumpWatcher _dumpWatcher = new();
    private readonly ContextMenuStrip _menu = new();
    private readonly ToolStripMenuItem _pauseItem;
    private HistoryForm? _historyForm;

    public TrayContext()
    {
        AppConfig.Load();
        SetPriority();

        _pauseItem = new ToolStripMenuItem("Pause for 1 hour", null, (_, _) => PauseOneHour());
        _pauseItem.Enabled = false;

        _icon = new NotifyIcon
        {
            Icon = MakeIcon(),
            Text = "CrashWatch — watching for system errors",
            Visible = true,
            ContextMenuStrip = BuildMenu(),
        };
        _icon.DoubleClick += (_, _) => ShowHistory();

        AppConfig.StartWatching(() =>
        {
            SetPriority();
            _watcher.Start(); // restart with the new rules
        });

        StartWatcher();
        StartDumpWatcher();
        BootSweep.Run(toast: true, history: _history);
    }

    private void StartDumpWatcher()
    {
        _dumpWatcher.DumpCreated += path =>
        {
            ToastService.Show("GPU kernel dump written",
                $"Windows captured a live kernel dump — the display driver reset (TDR).\n{path}",
                critical: true);
            _history.Add(DateTime.Now, "Critical", "LiveKernelReports", 0,
                $"GPU kernel dump: {path}");
        };
        _dumpWatcher.Start();
    }

    private void StartWatcher()
    {
        _watcher.RuleFired += OnRuleFired;
        _watcher.Start();
    }

    private void OnRuleFired(WatchRule rule, System.Diagnostics.Eventing.Reader.EventRecord rec)
    {
        try
        {
            var time = rec.TimeCreated ?? DateTime.Now;
            var detail = rec.FormatDescription();
            if (string.IsNullOrWhiteSpace(detail)) detail = rule.Detail;
            if (detail.Length > 300) detail = detail[..300] + "…";

            _history.Add(time, rule.Severity.ToString(), rule.Provider, rule.EventId, detail);

            if (AppConfig.Current.IsPaused) return;

            if (!_limiter.ShouldToast(rule, AppConfig.Current, out int burst)) return;

            bool critical = rule.Severity == EventSeverity.Critical;
            string prefix = critical && _history.CountInLast24h(rule.Provider, rule.EventId)
                                >= AppConfig.Current.WheaEscalationCount24h
                ? "RECURRING: "
                : "";
            string count = burst > 1 ? $" ({burst} in the last {AppConfig.Current.StorageAggregateMinutes} min)" : "";

            ToastService.Show($"{prefix}{rule.Title}{count}", detail, critical);
        }
        catch (Exception ex)
        {
            Log.Write("rule handler failed: " + ex.Message);
        }
    }

    private ContextMenuStrip BuildMenu()
    {
        _menu.Items.Clear();
        _menu.Items.Add("History", null, (_, _) => ShowHistory());
        _menu.Items.Add("Check now", null, (_, _) => { ToastService.Show("CrashWatch", "Checking for crashes since the last clean shutdown…", false); BootSweep.Run(toast: true, history: _history); });
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_pauseItem);
        _menu.Items.Add("Resume", null, (_, _) => Resume());
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("Exit", null, (_, _) => ExitThread());
        return _menu;
    }

    private void PauseOneHour()
    {
        AppConfig.Current.PauseUntilUtc = DateTime.UtcNow.AddHours(1);
        AppConfig.Current.Save();
        RefreshMenu();
    }

    private void Resume()
    {
        AppConfig.Current.PauseUntilUtc = null;
        AppConfig.Current.Save();
        RefreshMenu();
    }

    private void RefreshMenu()
    {
        _pauseItem.Enabled = !AppConfig.Current.IsPaused;
        _icon.Text = AppConfig.Current.IsPaused
            ? $"CrashWatch — paused until {AppConfig.Current.PauseUntilUtc?.ToLocalTime():HH:mm}"
            : "CrashWatch — watching for system errors";
    }

    private void ShowHistory()
    {
        if (_historyForm is { IsDisposed: false })
        {
            _historyForm.Activate();
            return;
        }
        _historyForm = new HistoryForm(_history);
        _historyForm.Show();
    }

    private static void SetPriority()
    {
        if (!AppConfig.Current.PriorityHigh) return;
        try
        {
            if (Process.GetCurrentProcess().PriorityClass != ProcessPriorityClass.High)
                Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High;
        }
        catch { }
    }

    private static Icon MakeIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var brush = new SolidBrush(Color.FromArgb(210, 30, 40));
            g.FillEllipse(brush, 2, 2, 28, 28);
            using var font = new Font("Segoe UI", 15f, FontStyle.Bold, GraphicsUnit.Pixel);
            var sz = g.MeasureString("!", font);
            g.DrawString("!", font, Brushes.White, 16 - sz.Width / 2, 16 - sz.Height / 2);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    protected override void ExitThreadCore()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _watcher.Dispose();
        _dumpWatcher.Dispose();
        base.ExitThreadCore();
    }
}
