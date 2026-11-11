using System.Net;

namespace GitHubTrafficTray;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly GitHubClient _gh;
    private readonly NotifyIcon _tray;
    private readonly ContextMenuStrip _menu;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly CancellationTokenSource _cts = new();
    private readonly Icon _icon;
    private readonly Font _boldFont;
    private readonly MainForm _main;
    private bool _exiting;

    public TrafficStore Store { get; }
    public AppSettings Settings { get; }
    public AppStatus Status { get; } = new();

    public event Action? DataChanged;
    public event Action? StatusChanged;

    public TrayApplicationContext(GitHubClient gh, string? userLogin)
    {
        _gh = gh;
        Status.UserLogin = userLogin;
        Status.TokenState = userLogin is null ? "nicht geprüft (beim Start nicht erreichbar)" : "gültig";

        Store = Storage.LoadStore(out string? storeWarning);
        Settings = Storage.LoadSettings(out string? settingsWarning);

        _icon = IconFactory.Create();
        _menu = new ContextMenuStrip();
        _boldFont = new Font(_menu.Font, FontStyle.Bold);
        _menu.Opening += (_, e) =>
        {
            BuildMenu();
            e.Cancel = false; // Ohne das bricht WinForms bei anfangs leerem Menü ab
        };
        BuildMenu();

        _tray = new NotifyIcon
        {
            Icon = _icon,
            ContextMenuStrip = _menu,
            Visible = true
        };
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) ShowMain();
        };

        _main = new MainForm(this, _icon);

        _timer = new System.Windows.Forms.Timer { Interval = 60_000 };
        _timer.Tick += (_, _) =>
        {
            CheckSchedule();
            OnStatusChanged();
        };
        _timer.Start();

        UpdateTrayText();

        foreach (var w in new[] { storeWarning, settingsWarning })
            if (w is not null)
                MessageBox.Show(w, AppConfig.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);

        _tray.ShowBalloonTip(3000, AppConfig.AppName,
            "Läuft im Infobereich. Linksklick öffnet die Übersicht, Rechtsklick zeigt Status und Beenden.",
            ToolTipIcon.Info);

        // Erste Prüfung kurz nach dem Start (holt einen versäumten Tagesabruf nach)
        var startTimer = new System.Windows.Forms.Timer { Interval = 2000 };
        startTimer.Tick += (_, _) =>
        {
            startTimer.Stop();
            startTimer.Dispose();
            CheckSchedule();
        };
        startTimer.Start();
    }

    // -----------------------------------------------------------------------
    // Zeitplan und Abruf
    // -----------------------------------------------------------------------

    private static string TodayLocalKey() => AppConfig.DateKey(DateTime.Now);

    private void CheckSchedule()
    {
        if (_exiting || Status.IsFetching) return;
        var now = DateTime.Now;
        if (Settings.LastScheduledFetchDate == TodayLocalKey()) return;
        if (now.Hour < Settings.FetchHour) return;
        if (Status.NextRetryLocal is DateTime retry && now < retry) return;
        _ = RunFetchAsync(scheduled: true);
    }

    public async Task RunFetchAsync(bool scheduled)
    {
        if (Status.IsFetching || _exiting) return;

        Status.IsFetching = true;
        Status.LastStartLocal = DateTime.Now;
        Status.LastKind = scheduled ? "geplant" : "manuell";
        Status.Progress = "Starte …";
        OnStatusChanged();

        var progress = new Progress<string>(msg =>
        {
            if (_exiting) return;
            Status.Progress = msg;
            OnStatusChanged();
        });

        try
        {
            var result = await TrafficService.FetchAsync(_gh, progress, _cts.Token);
            var nowUtc = DateTime.UtcNow;
            var newRepos = Store.Merge(result, nowUtc);
            Storage.SaveStore(Store);

            Status.LastSuccess = true;
            Status.LastError = null;
            Status.RepoErrors = result.Errors;
            Status.NewRepos = newRepos;
            Status.NextRetryLocal = null;
            Status.TokenState = "gültig";

            Settings.LastSuccessfulFetchUtc = nowUtc;
            if (scheduled) Settings.LastScheduledFetchDate = TodayLocalKey();
            Storage.SaveSettings(Settings);

            DataChanged?.Invoke();
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
            // Programm wird beendet
        }
        catch (Exception ex)
        {
            Status.LastSuccess = false;
            Status.LastError = TrafficService.Describe(ex);
            Status.RepoErrors = new List<string>();
            if (ex is GitHubApiException { StatusCode: HttpStatusCode.Unauthorized })
                Status.TokenState = "ungültig oder abgelaufen (401) – Programm neu starten und Token neu eingeben";
            if (scheduled)
                Status.NextRetryLocal = DateTime.Now.AddMinutes(AppConfig.RetryMinutesAfterError);
        }
        finally
        {
            Status.IsFetching = false;
            Status.Progress = null;
            Status.LastEndLocal = DateTime.Now;
            if (!_exiting) OnStatusChanged();
        }
    }

    public void SetFetchHour(int hour)
    {
        if (hour < 1 || hour > 23) return;
        Settings.FetchHour = hour;
        Storage.SaveSettings(Settings);
        OnStatusChanged();
    }

    /// <summary>Erster Tag (UTC), der beim Löschen erhalten bleibt.</summary>
    public static DateTime PurgeCutoffDateUtc() => DateTime.UtcNow.Date.AddDays(-(AppConfig.PurgeKeepDays - 1));

    public int PurgeOldData()
    {
        int removed = Store.PurgeBefore(AppConfig.DateKey(PurgeCutoffDateUtc()));
        Storage.SaveStore(Store);
        DataChanged?.Invoke();
        OnStatusChanged();
        return removed;
    }

    public string NextFetchText()
    {
        if (Status.IsFetching) return "läuft gerade";
        var now = DateTime.Now;
        string hour = $"{Settings.FetchHour:00}:00";
        if (Settings.LastScheduledFetchDate == TodayLocalKey()) return $"morgen, {hour}";
        if (Status.NextRetryLocal is DateTime retry && now < retry) return $"Wiederholung um {retry:HH:mm}";
        if (now.Hour < Settings.FetchHour) return $"heute, {hour}";
        return "fällig (innerhalb einer Minute)";
    }

    public string LastSuccessText() =>
        Settings.LastSuccessfulFetchUtc is DateTime t ? t.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss") : "noch nie";

    public string RateLimitText()
    {
        var rl = _gh.RateLimit;
        return rl is null ? "unbekannt" : $"{rl.Remaining:N0} / {rl.Limit:N0} frei, Reset {rl.ResetLocal:HH:mm}";
    }

    // -----------------------------------------------------------------------
    // Tray und Menü
    // -----------------------------------------------------------------------

    private void OnStatusChanged()
    {
        if (_exiting) return;
        UpdateTrayText();
        StatusChanged?.Invoke();
    }

    private void UpdateTrayText()
    {
        string line2 = Status.IsFetching
            ? "Abruf läuft …"
            : Status.LastSuccess == false
                ? "Letzter Abruf fehlgeschlagen"
                : "Letzter Abruf: " + LastSuccessText();
        string text = $"GitHub Traffic ({AppConfig.Owner})\n{line2}";
        _tray.Text = text.Length > 127 ? text[..127] : text;
    }

    private (int Clones14, int Views14, long ClonesAll, long ViewsAll) ComputeSums()
    {
        string cutoff = AppConfig.DateKey(DateTime.UtcNow.Date.AddDays(-13));
        int c14 = 0, v14 = 0;
        long cAll = 0, vAll = 0;
        foreach (var rec in Store.Repos.Values)
        {
            foreach (var kv in rec.Clones)
            {
                cAll += kv.Value.Count;
                if (string.CompareOrdinal(kv.Key, cutoff) >= 0) c14 += kv.Value.Count;
            }
            foreach (var kv in rec.Views)
            {
                vAll += kv.Value.Count;
                if (string.CompareOrdinal(kv.Key, cutoff) >= 0) v14 += kv.Value.Count;
            }
        }
        return (c14, v14, cAll, vAll);
    }

    private static string Trunc(string s, int max = 150) => s.Length <= max ? s : s[..(max - 1)] + "…";

    private ToolStripLabel MenuLabel(string text, bool bold = false)
    {
        var l = new ToolStripLabel(Trunc(text)) { Margin = new Padding(4, 2, 4, 2) };
        if (bold) l.Font = _boldFont;
        return l;
    }

    private void BuildMenu()
    {
        var old = _menu.Items.Cast<ToolStripItem>().ToList();
        _menu.Items.Clear();
        foreach (var item in old) item.Dispose();

        var sums = ComputeSums();

        _menu.Items.Add(MenuLabel($"GitHub Traffic – {AppConfig.Owner}", bold: true));
        _menu.Items.Add(new ToolStripSeparator());

        _menu.Items.Add(MenuLabel(Status.IsFetching
            ? $"Status: Abruf läuft – {Status.Progress}"
            : "Status: bereit"));

        if (Status.LastEndLocal is DateTime end)
        {
            string result = Status.LastSuccess == true
                ? (Status.RepoErrors.Count == 0 ? "erfolgreich" : $"erfolgreich mit {Status.RepoErrors.Count} Fehler(n)")
                : "fehlgeschlagen";
            _menu.Items.Add(MenuLabel($"Letzter Abruf (diese Sitzung): {end:dd.MM.yyyy HH:mm:ss} – {result} ({Status.LastKind})"));
        }
        else
        {
            _menu.Items.Add(MenuLabel("Letzter Abruf (diese Sitzung): noch keiner"));
        }

        _menu.Items.Add(MenuLabel($"Letzter erfolgreicher Abruf: {LastSuccessText()}"));
        _menu.Items.Add(MenuLabel($"Nächster Abruf: {NextFetchText()}  (täglich um {Settings.FetchHour:00}:00)"));
        _menu.Items.Add(MenuLabel($"Erfasste Repos: {Store.Repos.Count}"));

        if (Status.NewRepos.Count > 0)
            _menu.Items.Add(MenuLabel($"Neu beim letzten Abruf: {string.Join(", ", Status.NewRepos)}"));

        string user = Status.UserLogin is null ? "" : $" (Benutzer: {Status.UserLogin})";
        _menu.Items.Add(MenuLabel($"Token: {Status.TokenState}{user}"));
        _menu.Items.Add(MenuLabel($"Rate-Limit: {RateLimitText()}"));
        _menu.Items.Add(MenuLabel($"Clones: {sums.Clones14:N0} (letzte 14 Tage) · {sums.ClonesAll:N0} (gesamt gespeichert)"));
        _menu.Items.Add(MenuLabel($"Views: {sums.Views14:N0} (letzte 14 Tage) · {sums.ViewsAll:N0} (gesamt gespeichert)"));
        _menu.Items.Add(MenuLabel($"Datenordner: {AppConfig.DataDirectory}"));

        var errors = new List<string>();
        if (Status.LastError is not null) errors.Add("Abruf: " + Status.LastError);
        errors.AddRange(Status.RepoErrors);

        if (errors.Count == 0)
        {
            _menu.Items.Add(MenuLabel("Fehler: keine"));
        }
        else
        {
            var errMenu = new ToolStripMenuItem($"Fehler beim letzten Abruf ({errors.Count})") { ForeColor = Theme.Danger };
            foreach (var err in errors) errMenu.DropDownItems.Add(MenuLabel(err));
            errMenu.DropDownItems.Add(new ToolStripSeparator());
            errMenu.DropDownItems.Add("Fehler in Zwischenablage kopieren", null, (_, _) =>
            {
                try { Clipboard.SetText(string.Join(Environment.NewLine, errors)); } catch { /* Zwischenablage belegt */ }
            });
            _menu.Items.Add(errMenu);
        }

        _menu.Items.Add(new ToolStripSeparator());

        var open = new ToolStripMenuItem("Übersicht öffnen", null, (_, _) => ShowMain()) { Font = _boldFont };
        _menu.Items.Add(open);

        var fetch = new ToolStripMenuItem("Jetzt abrufen", null, async (_, _) => await RunFetchAsync(scheduled: false))
        {
            Enabled = !Status.IsFetching
        };
        _menu.Items.Add(fetch);

        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(new ToolStripMenuItem("Beenden", null, (_, _) => ExitApp()));
    }

    private void ShowMain()
    {
        if (_exiting) return;
        _main.RefreshAll();
        if (!_main.Visible) _main.Show();
        if (_main.WindowState == FormWindowState.Minimized) _main.WindowState = FormWindowState.Normal;
        _main.Activate();
    }

    private void ExitApp()
    {
        if (_exiting) return;
        _exiting = true;
        _cts.Cancel();
        _timer.Stop();
        _tray.Visible = false;
        _main.AllowClose = true;
        if (_main.IsHandleCreated) _main.Close();
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _tray.Dispose();
            _menu.Dispose();
            _main.Dispose();
            _boldFont.Dispose();
            _gh.Dispose();
            _cts.Dispose();
        }
        base.Dispose(disposing);
    }
}
