namespace CrashWatch;

internal sealed class HistoryForm : Form
{
    private readonly HistoryStore _store;
    private readonly ListView _list;
    private readonly Label _status;

    public HistoryForm(HistoryStore store)
    {
        _store = store;

        Text = "CrashWatch — Event History";
        Size = new Size(920, 520);
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(700, 350);

        _list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            HideSelection = false,
        };
        _list.Columns.Add("Time", 135);
        _list.Columns.Add("Severity", 80);
        _list.Columns.Add("Source", 210);
        _list.Columns.Add("Event", 60);
        _list.Columns.Add("Message", 380);

        foreach (var e in _store.Entries.OrderBy(x => x.Time))
            AddRow(e);

        _store.Added += OnAdded;
        _store.Cleared += OnCleared;

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 34 };
        _status = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(6, 8, 0, 0),
        };
        var clearButton = new Button
        {
            Dock = DockStyle.Right,
            Width = 110,
            Text = "Clear list",
        };
        clearButton.Click += (_, _) =>
        {
            if (MessageBox.Show(this,
                    "Clear all entries from the list and history file?",
                    "CrashWatch", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                _store.Clear();
            }
        };
        bottom.Controls.Add(_status);
        bottom.Controls.Add(clearButton);

        Controls.Add(_list);
        Controls.Add(bottom);
        RefreshStatus();
        FormClosed += (_, _) =>
        {
            _store.Added -= OnAdded;
            _store.Cleared -= OnCleared;
        };
    }

    private void OnAdded(HistoryEntry e)
    {
        if (InvokeRequired) BeginInvoke(() => AddRow(e));
        else AddRow(e);
    }

    private void OnCleared()
    {
        if (InvokeRequired) BeginInvoke(RefreshList);
        else RefreshList();
    }

    private void AddRow(HistoryEntry e)
    {
        _list.Items.Insert(0, MakeItem(e));
        if (_list.Items.Count > 500)
            _list.Items.RemoveAt(_list.Items.Count - 1);
        RefreshStatus();
    }

    private void RefreshList()
    {
        _list.Items.Clear();
        foreach (var e in _store.Entries.OrderBy(x => x.Time))
            _list.Items.Add(MakeItem(e));
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        _status.Text = $"{_store.Entries.Count} events · csv: {HistoryStore.FilePath}";
    }

    private static ListViewItem MakeItem(HistoryEntry e)
    {
        var item = new ListViewItem(new[]
        {
            e.Time.ToLocalTime().ToString("MM-dd HH:mm:ss"),
            e.Severity,
            e.Source,
            e.EventId.ToString(),
            e.Message,
        });

        if (string.Equals(e.Severity, "Critical", StringComparison.OrdinalIgnoreCase))
            item.ForeColor = Color.Firebrick;

        return item;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _store.Added -= OnAdded;
            _store.Cleared -= OnCleared;
        }
        base.Dispose(disposing);
    }
}
