using System.ComponentModel;

namespace GitHubTrafficTray;

internal sealed class MainForm : Form
{
    private sealed record RepoItem(string? Name, string Text)
    {
        public override string ToString() => Text;
    }

    private sealed record RangeItem(int? Days, string Text)
    {
        public override string ToString() => Text;
    }

    private sealed record HourItem(int Hour)
    {
        public override string ToString() => $"{Hour:00}:00 Uhr";
    }

    private readonly TrayApplicationContext _app;
    private readonly ComboBox _cmbRepo;
    private readonly ComboBox _cmbRange;
    private readonly ComboBox _cmbHour;
    private readonly Button _btnFetch;
    private readonly Button _btnPurge;
    private readonly LineChart _chClones = new() { Dock = DockStyle.Fill, Margin = new Padding(6) };
    private readonly LineChart _chCloners = new() { Dock = DockStyle.Fill, Margin = new Padding(6) };
    private readonly LineChart _chViews = new() { Dock = DockStyle.Fill, Margin = new Padding(6) };
    private readonly LineChart _chVisitors = new() { Dock = DockStyle.Fill, Margin = new Padding(6) };
    private readonly GroupBox _grpRef;
    private readonly GroupBox _grpPaths;
    private readonly DataGridView _gridRef;
    private readonly DataGridView _gridPaths;
    private readonly Label _statusLabel;
    private bool _loading;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool AllowClose { get; set; }

    public MainForm(TrayApplicationContext app, Icon icon)
    {
        _app = app;

        Text = $"GitHub Traffic – {AppConfig.Owner}";
        Icon = icon;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1280, 900);
        MinimumSize = new Size(960, 700);
        BackColor = Theme.Back;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 9f);

        // --- Obere Leiste --------------------------------------------------
        var top = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true,
            Padding = new Padding(8, 8, 8, 4)
        };

        _cmbRepo = MakeCombo(300);
        _cmbRange = MakeCombo(150);
        _cmbHour = MakeCombo(110);
        _btnFetch = MakeButton("Jetzt abrufen");
        _btnPurge = MakeButton($"Alles außer den letzten {AppConfig.PurgeKeepDays} Tagen löschen");
        _btnPurge.ForeColor = ColorTranslator.FromHtml("#ff7b72");

        top.Controls.Add(MakeLabel("Repo:"));
        top.Controls.Add(_cmbRepo);
        top.Controls.Add(MakeLabel("Zeitraum:"));
        top.Controls.Add(_cmbRange);
        top.Controls.Add(MakeLabel("Täglicher Abruf um:"));
        top.Controls.Add(_cmbHour);
        top.Controls.Add(_btnFetch);
        top.Controls.Add(_btnPurge);

        // --- Diagramme -----------------------------------------------------
        var charts = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(4) };
        charts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        charts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        charts.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        charts.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        charts.Controls.Add(_chClones, 0, 0);
        charts.Controls.Add(_chCloners, 1, 0);
        charts.Controls.Add(_chViews, 0, 1);
        charts.Controls.Add(_chVisitors, 1, 1);

        // --- Tabellen ------------------------------------------------------
        _gridRef = MakeGrid(("Referrer", 60, false), ("Views", 20, true), ("Unique", 20, true));
        _gridPaths = MakeGrid(("Pfad", 45, false), ("Titel", 35, false), ("Views", 10, true), ("Unique", 10, true));
        _grpRef = MakeGroup("Top-Referrer", _gridRef);
        _grpPaths = MakeGroup("Beliebte Pfade", _gridPaths);

        var tables = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(4) };
        tables.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        tables.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        tables.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        tables.Controls.Add(_grpRef, 0, 0);
        tables.Controls.Add(_grpPaths, 1, 0);

        // --- Statuszeile ---------------------------------------------------
        _statusLabel = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            BackColor = Theme.Panel,
            ForeColor = Theme.Muted,
            Padding = new Padding(8, 5, 8, 5),
            Margin = new Padding(0)
        };

        // --- Gesamtlayout --------------------------------------------------
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 64));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 36));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(top, 0, 0);
        root.Controls.Add(charts, 0, 1);
        root.Controls.Add(tables, 0, 2);
        root.Controls.Add(_statusLabel, 0, 3);
        Controls.Add(root);

        // --- Auswahllisten befüllen ----------------------------------------
        _loading = true;
        _cmbRange.Items.AddRange(new object[]
        {
            new RangeItem(14, "Letzte 14 Tage"),
            new RangeItem(30, "Letzte 30 Tage"),
            new RangeItem(90, "Letzte 90 Tage"),
            new RangeItem(365, "Letzte 365 Tage"),
            new RangeItem(null, "Gesamter Zeitraum")
        });
        _cmbRange.SelectedIndex = 0;

        for (int h = 1; h <= 23; h++) _cmbHour.Items.Add(new HourItem(h));
        _cmbHour.SelectedIndex = Math.Clamp(_app.Settings.FetchHour, 1, 23) - 1;
        _loading = false;

        ReloadRepoList();

        // --- Ereignisse ----------------------------------------------------
        _cmbRepo.SelectedIndexChanged += (_, _) => { if (!_loading) RefreshView(); };
        _cmbRange.SelectedIndexChanged += (_, _) => { if (!_loading) RefreshView(); };
        _cmbHour.SelectedIndexChanged += (_, _) =>
        {
            if (!_loading && _cmbHour.SelectedItem is HourItem hi) _app.SetFetchHour(hi.Hour);
        };
        _btnFetch.Click += async (_, _) => await FetchNowAsync();
        _btnPurge.Click += (_, _) => PurgeOld();

        _app.DataChanged += () =>
        {
            ReloadRepoList();
            RefreshView();
        };
        _app.StatusChanged += UpdateStatus;

        RefreshView();
        UpdateStatus();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.ApplyDarkTitleBar(Handle);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // X-Button: nur ausblenden, Programm läuft im Tray weiter
        if (!AllowClose && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnFormClosing(e);
    }

    public void RefreshAll()
    {
        ReloadRepoList();
        RefreshView();
        UpdateStatus();
    }

    // -----------------------------------------------------------------------
    // Aktionen
    // -----------------------------------------------------------------------

    private async Task FetchNowAsync()
    {
        await _app.RunFetchAsync(scheduled: false);
        if (_app.Status.LastSuccess == false && Visible)
        {
            MessageBox.Show(this, "Abruf fehlgeschlagen:\n\n" + _app.Status.LastError,
                AppConfig.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void PurgeOld()
    {
        var cutoff = TrayApplicationContext.PurgeCutoffDateUtc();
        var answer = MessageBox.Show(this,
            $"Alle gespeicherten Tageswerte sowie Referrer- und Pfad-Stände vor dem {cutoff:dd.MM.yyyy} " +
            $"werden endgültig gelöscht.\n\nDie letzten {AppConfig.PurgeKeepDays} Tage " +
            $"({cutoff:dd.MM.yyyy} bis heute) bleiben erhalten.\n\nFortfahren?",
            "Alte Daten löschen", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes) return;

        try
        {
            int removed = _app.PurgeOldData();
            MessageBox.Show(this, $"{removed:N0} Einträge gelöscht.", AppConfig.AppName,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Löschen fehlgeschlagen:\n\n" + ex.Message, AppConfig.AppName,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // -----------------------------------------------------------------------
    // Anzeige
    // -----------------------------------------------------------------------

    private void ReloadRepoList()
    {
        string? selected = (_cmbRepo.SelectedItem as RepoItem)?.Name;
        _loading = true;
        _cmbRepo.BeginUpdate();
        _cmbRepo.Items.Clear();
        _cmbRepo.Items.Add(new RepoItem(null, $"Gesamt (alle {_app.Store.Repos.Count} Repos)"));

        int selectIndex = 0;
        foreach (var rec in _app.Store.Repos.Values.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase))
        {
            var tags = new List<string>();
            if (rec.IsPrivate) tags.Add("privat");
            if (rec.IsFork) tags.Add("Fork");
            if (rec.IsArchived) tags.Add("archiviert");
            if (_app.Store.LastCompleteRepoListUtc is DateTime listed && rec.LastSeenUtc < listed)
                tags.Add("nicht mehr gefunden");
            string text = tags.Count == 0 ? rec.Name : $"{rec.Name}  ({string.Join(", ", tags)})";
            int idx = _cmbRepo.Items.Add(new RepoItem(rec.Name, text));
            if (rec.Name == selected) selectIndex = idx;
        }

        _cmbRepo.SelectedIndex = selectIndex;
        _cmbRepo.EndUpdate();
        _loading = false;
    }

    private void RefreshView()
    {
        string? repoName = (_cmbRepo.SelectedItem as RepoItem)?.Name;
        int? days = _cmbRange.SelectedItem is RangeItem ri ? ri.Days : 14;

        var repos = repoName is null
            ? _app.Store.Repos.Values.ToList()
            : _app.Store.Repos.TryGetValue(repoName, out var one) ? new List<RepoRecord> { one } : new List<RepoRecord>();

        // Zeitraum (UTC-Tage, wie GitHub)
        DateTime to = DateTime.UtcNow.Date;
        DateTime from;
        if (days is int d)
        {
            from = to.AddDays(-(d - 1));
        }
        else
        {
            var keys = repos.SelectMany(r => r.Clones.Keys.Concat(r.Views.Keys)).ToList();
            from = keys.Count == 0 ? to.AddDays(-13) : AppConfig.ParseDateKey(keys.Min(StringComparer.Ordinal)!);
            if (from > to) from = to;
        }

        var clones = BuildSeries(repos, r => r.Clones, s => s.Count, from, to);
        var cloners = BuildSeries(repos, r => r.Clones, s => s.Uniques, from, to);
        var views = BuildSeries(repos, r => r.Views, s => s.Count, from, to);
        var visitors = BuildSeries(repos, r => r.Views, s => s.Uniques, from, to);

        string rangeText = days is int dd ? $"letzte {dd} Tage" : "gesamter Zeitraum";
        var single = repos.Count == 1 && repoName is not null ? repos[0] : null;
        bool useGitHubUniques = single is not null && days == 14;

        _chClones.SetData($"Clones – {rangeText}", $"{clones.Sum(p => p.Value):N0} Clones", "Clones", clones);
        _chCloners.SetData($"Unique cloners – {rangeText}",
            UniqueSubtitle(useGitHubUniques ? single!.LastClones14 : null, cloners, "Unique cloners"),
            "Cloners", cloners);
        _chViews.SetData($"Views – {rangeText}", $"{views.Sum(p => p.Value):N0} Views", "Views", views);
        _chVisitors.SetData($"Unique visitors – {rangeText}",
            UniqueSubtitle(useGitHubUniques ? single!.LastViews14 : null, visitors, "Unique visitors"),
            "Visitors", visitors);

        FillReferrers(repos);
        FillPaths(repos);
    }

    private static string UniqueSubtitle(Totals14? gitHubTotals, List<ChartPoint> series, string label)
    {
        if (gitHubTotals is not null)
            return $"{gitHubTotals.Uniques:N0} {label} (laut GitHub, Stand {gitHubTotals.FetchedUtc.ToLocalTime():dd.MM.yyyy})";
        return $"{series.Sum(p => p.Value):N0} (Summe der täglichen Unique-Werte)";
    }

    private static List<ChartPoint> BuildSeries(
        List<RepoRecord> repos,
        Func<RepoRecord, SortedDictionary<string, DayStat>> select,
        Func<DayStat, int> value,
        DateTime from,
        DateTime to)
    {
        var sums = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var r in repos)
            foreach (var kv in select(r))
                sums[kv.Key] = sums.GetValueOrDefault(kv.Key) + value(kv.Value);

        var list = new List<ChartPoint>();
        for (var day = from; day <= to; day = day.AddDays(1))
            list.Add(new ChartPoint(day, sums.GetValueOrDefault(AppConfig.DateKey(day))));
        return list;
    }

    private void FillReferrers(List<RepoRecord> repos)
    {
        var agg = new Dictionary<string, (int Count, int Uniques)>(StringComparer.OrdinalIgnoreCase);
        string? stand = null;
        foreach (var rec in repos)
        {
            if (rec.ReferrerSnapshots.Count == 0) continue;
            var last = rec.ReferrerSnapshots.Last();
            if (stand is null || string.CompareOrdinal(last.Key, stand) > 0) stand = last.Key;
            foreach (var e in last.Value)
            {
                var cur = agg.GetValueOrDefault(e.Referrer);
                agg[e.Referrer] = (cur.Count + e.Count, cur.Uniques + e.Uniques);
            }
        }

        _gridRef.Rows.Clear();
        foreach (var kv in agg.OrderByDescending(k => k.Value.Count).ThenByDescending(k => k.Value.Uniques))
            _gridRef.Rows.Add(kv.Key, kv.Value.Count, kv.Value.Uniques);

        _grpRef.Text = stand is null
            ? "Top-Referrer – noch keine Daten"
            : $"Top-Referrer – Stand {AppConfig.ParseDateKey(stand):dd.MM.yyyy} (GitHub: letzte 14 Tage)";
    }

    private void FillPaths(List<RepoRecord> repos)
    {
        var rows = new List<PathEntry>();
        string? stand = null;
        foreach (var rec in repos)
        {
            if (rec.PathSnapshots.Count == 0) continue;
            var last = rec.PathSnapshots.Last();
            if (stand is null || string.CompareOrdinal(last.Key, stand) > 0) stand = last.Key;
            rows.AddRange(last.Value);
        }

        _gridPaths.Rows.Clear();
        foreach (var p in rows.OrderByDescending(p => p.Count).ThenByDescending(p => p.Uniques))
            _gridPaths.Rows.Add(p.Path, p.Title, p.Count, p.Uniques);

        _grpPaths.Text = stand is null
            ? "Beliebte Pfade – noch keine Daten"
            : $"Beliebte Pfade – Stand {AppConfig.ParseDateKey(stand):dd.MM.yyyy} (GitHub: letzte 14 Tage)";
    }

    private void UpdateStatus()
    {
        var s = _app.Status;
        _btnFetch.Enabled = !s.IsFetching;
        _btnFetch.Text = s.IsFetching ? "Abruf läuft …" : "Jetzt abrufen";

        string text = s.IsFetching
            ? $"Abruf läuft: {s.Progress}"
            : $"Repos: {_app.Store.Repos.Count}   ·   Letzter erfolgreicher Abruf: {_app.LastSuccessText()}   ·   " +
              $"Nächster Abruf: {_app.NextFetchText()}   ·   Rate-Limit: {_app.RateLimitText()}";

        int errorCount = s.RepoErrors.Count + (s.LastError is null ? 0 : 1);
        if (!s.IsFetching && errorCount > 0)
            text += $"   ·   {errorCount} Fehler (Details per Rechtsklick auf das Tray-Symbol)";

        _statusLabel.Text = text;
        _statusLabel.ForeColor = !s.IsFetching && errorCount > 0 ? ColorTranslator.FromHtml("#ff7b72") : Theme.Muted;
    }

    // -----------------------------------------------------------------------
    // UI-Helfer
    // -----------------------------------------------------------------------

    private static Label MakeLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = Theme.Text,
        Margin = new Padding(10, 8, 3, 3)
    };

    private static ComboBox MakeCombo(int width) => new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        FlatStyle = FlatStyle.Flat,
        BackColor = Theme.Panel,
        ForeColor = Theme.Text,
        Width = width,
        Margin = new Padding(3, 4, 3, 3)
    };

    private static Button MakeButton(string text)
    {
        var b = new Button
        {
            Text = text,
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Button,
            ForeColor = Theme.Text,
            Padding = new Padding(8, 2, 8, 2),
            Margin = new Padding(10, 2, 3, 3),
            UseVisualStyleBackColor = false
        };
        b.FlatAppearance.BorderColor = Theme.Border;
        return b;
    }

    private static GroupBox MakeGroup(string text, Control content)
    {
        var g = new GroupBox
        {
            Text = text,
            Dock = DockStyle.Fill,
            ForeColor = Theme.Text,
            BackColor = Theme.Back,
            Margin = new Padding(6),
            Padding = new Padding(8)
        };
        g.Controls.Add(content);
        return g;
    }

    private static DataGridView MakeGrid(params (string Header, float Weight, bool Numeric)[] columns)
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = Theme.Back,
            BorderStyle = BorderStyle.None,
            GridColor = Theme.Border,
            EnableHeadersVisualStyles = false,
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize
        };
        grid.ColumnHeadersDefaultCellStyle.BackColor = Theme.Panel;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Theme.Text;
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Theme.Panel;
        grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Theme.Text;
        grid.DefaultCellStyle.BackColor = Theme.Back;
        grid.DefaultCellStyle.ForeColor = Theme.Text;
        grid.DefaultCellStyle.SelectionBackColor = Theme.Selection;
        grid.DefaultCellStyle.SelectionForeColor = Color.White;

        foreach (var (header, weight, numeric) in columns)
        {
            var col = new DataGridViewTextBoxColumn
            {
                HeaderText = header,
                FillWeight = weight,
                SortMode = DataGridViewColumnSortMode.Automatic
            };
            if (numeric)
            {
                col.ValueType = typeof(int);
                col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
                col.DefaultCellStyle.Format = "N0";
            }
            grid.Columns.Add(col);
        }
        return grid;
    }
}
