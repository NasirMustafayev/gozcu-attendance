using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using GozcuStandalone.Device;
using GozcuStandalone.Reporting;
using GozcuStandalone.Storage;
using Microsoft.Win32;

namespace GozcuStandalone.App;

public partial class MainWindow : Window
{
    private const string AutostartName = "GozcuAttendance";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private readonly AppSettings _settings = AppSettings.Load();
    private readonly AttendanceStore _store = new(AppPaths.Database);
    private readonly DeviceSync _sync;
    private readonly DispatcherTimer _syncTimer = new();
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private readonly ObservableCollection<PersonRow> _people = new();
    private readonly TrayIcon _tray;
    private readonly DispatcherTimer _importTimer = new();
    private readonly DispatcherTimer _webSyncTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private long _lastWebRequest;
    private bool _webSyncBusy;
    private bool _importBusy;
    private bool _loadingDepts;

    private List<DailyRow> _summaryRows = new();
    private List<AttendanceRecord> _scanRows = new();
    private bool _busy;
    private bool _ready;
    private bool _exiting;
    private bool _startHidden;
    private bool? _online;
    private int _pending;
    private string _serial = "";
    private DateTime? _lastSync;

    public MainWindow(bool startMinimized)
    {
        _sync = new DeviceSync(_settings, _store);
        AppServices.Settings = _settings;
        AppServices.Store = _store;
        AppServices.Sync = _sync;

        InitializeComponent();
        RestoreWindowState();
        _startHidden = startMinimized;
        if (startMinimized && !_settings.MinimizeToTray) WindowState = WindowState.Minimized;

        _tray = new TrayIcon(ShowFromTray, () => _ = SyncNowAsync(manual: true), ExitApp);

        PeopleGrid.ItemsSource = _people;
        _syncTimer.Tick += async (_, _) => await SyncNowAsync(manual: false);
        _toastTimer.Tick += (_, _) => { Toast.Visibility = Visibility.Collapsed; _toastTimer.Stop(); };
        _importTimer.Tick += async (_, _) => await AutoImportAsync();
        _webSyncTimer.Tick += async (_, _) => await AnswerWebSyncAsync();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        FillSettingsFields();
        FromPicker.SelectedDate = DateTime.Today.AddDays(-6);
        ToPicker.SelectedDate = DateTime.Today;
        _ready = true;

        AppServices.DataChanged += OnDataChanged;
        AppServices.ToastRequested += ShowToast;

        MakeCardClickable(PresentValue, "present", "Click to see who has scanned in");
        MakeCardClickable(NotInValue, "notin", "Click to see who is expected but not in");
        MakeCardClickable(LateValue, "late", "Click to see who is late");
        MakeCardClickable(EarlyValue, "early", "Click to see who left early");
        MakeCardClickable(ScansValue, "scans", "Click to see today's scans");
        MakeCardClickable(PendingValue, "pending", "Click to sync with the device now");
        MakeCardClickable(NotInList, "notin", "Click to see the full list");
        MakeCardClickable(FeedList, "scans", "Click to see today's scans");

        AppServices.Log("Info", "App", $"Attendance {AppVersion} started.");
        try { _store.PruneLog(Math.Max(7, _settings.LogKeepDays)); } catch (Exception) { }
        _ = Task.Run(() =>
        {
            try
            {
                var made = Maintenance.RunDaily(_store);
                if (made is not null) AppServices.Log("Info", "Backup", "Daily backup saved: " + Path.GetFileName(made));
            }
            catch (Exception ex) { AppServices.Log("Warn", "Backup", "Daily backup failed: " + ex.GetBaseException().Message); }
        });

        // Requests left before this app started are old news; answer only new ones.
        try { _lastWebRequest = _store.GetSyncRequest()?.Id ?? 0; } catch (Exception) { }
        _webSyncTimer.Start();

        ApplyTimer();
        LoadDepartments();
        RefreshDashboard();
        LoadPeople();
        LoadAttendance();

        if (_startHidden && _settings.MinimizeToTray) Hide();
        await SyncNowAsync(manual: false);
    }

    // ---------- clickable dashboard cards ----------

    // Turns a dashboard card into a button that opens the list of people behind its number.
    private void MakeCardClickable(DependencyObject inside, string kind, string tip)
    {
        DependencyObject? node = inside;
        while (node is not null and not Border) node = LogicalTreeHelper.GetParent(node);
        if (node is not Border card) return;

        card.Style = (Style)FindResource("ClickCard");
        card.Tag = kind;
        card.ToolTip = tip;
        card.MouseLeftButtonUp += Card_Click;
    }

    private async void Card_Click(object sender, MouseButtonEventArgs e)
    {
        var kind = (sender as FrameworkElement)?.Tag as string;
        if (kind is null) return;

        if (kind == "pending")
        {
            await SyncNowAsync(manual: true);
            return;
        }

        var win = new DetailsWindow(Reports.Today(AppServices.Context(), kind)) { Owner = this };
        win.ShowDialog();
        if (win.OpenPersonId is { } id)
        {
            NavReports.IsChecked = true;
            PageReports.ShowTimesheet(id);
        }
    }

    private static string AppVersion => typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    // ---------- window behaviour ----------

    public void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private void ExitApp()
    {
        _exiting = true;
        Close();
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        SaveWindowState();

        if (!_exiting && _settings.MinimizeToTray)
        {
            e.Cancel = true;
            Hide();
            if (!_settings.TrayHintShown)
            {
                _settings.TrayHintShown = true;
                _settings.Save();
                _tray.Balloon("Attendance is still running", "It keeps syncing from the tray. Right-click the icon to exit.");
            }
            return;
        }

        AppServices.Log("Info", "App", "Attendance closed.");
        _tray.Dispose();
    }

    private void RestoreWindowState()
    {
        if (_settings.WindowWidth is not { } w || _settings.WindowHeight is not { } h) return;
        Width = Math.Max(MinWidth, w);
        Height = Math.Max(MinHeight, h);

        if (_settings.WindowLeft is { } l && _settings.WindowTop is { } t
            && l > SystemParameters.VirtualScreenLeft - 50 && t > SystemParameters.VirtualScreenTop - 10
            && l < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 120
            && t < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 80)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = l;
            Top = t;
        }
        if (_settings.WindowMaximized) WindowState = WindowState.Maximized;
    }

    private void SaveWindowState()
    {
        var b = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        if (b.IsEmpty || b.Width < 100) return;
        _settings.WindowLeft = b.Left;
        _settings.WindowTop = b.Top;
        _settings.WindowWidth = b.Width;
        _settings.WindowHeight = b.Height;
        _settings.WindowMaximized = WindowState == WindowState.Maximized;
        try { _settings.Save(); } catch (IOException) { }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        if (e.Key == Key.F5) { _ = SyncNowAsync(manual: true); e.Handled = true; return; }
        if (!ctrl) return;

        RadioButton? target = e.Key switch
        {
            Key.D1 => NavDashboard, Key.D2 => NavAttendance, Key.D3 => NavSchedule, Key.D4 => NavReports,
            Key.D5 => NavPeople, Key.D6 => NavLog, Key.D7 => NavSettings, _ => null,
        };
        if (target is not null) { target.IsChecked = true; e.Handled = true; }
        else if (e.Key == Key.N) { NavAttendance.IsChecked = true; AddManual_Click(this, e); e.Handled = true; }
        else if (e.Key == Key.Q) { ExitApp(); e.Handled = true; }
    }

    // ---------- navigation ----------

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        PageDashboard.Visibility = NavDashboard.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PageAttendance.Visibility = NavAttendance.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PageSchedule.Visibility = NavSchedule.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PageReports.Visibility = NavReports.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PagePeople.Visibility = NavPeople.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PageLog.Visibility = NavLog.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PageSettings.Visibility = NavSettings.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

        if (NavAttendance.IsChecked == true) LoadAttendance();
        if (NavPeople.IsChecked == true) LoadPeople();
        if (NavSettings.IsChecked == true) RefreshSettingsInfo();
    }

    private void OnDataChanged()
    {
        LoadDepartments();
        RefreshDashboard();
        if (PageAttendance.Visibility == Visibility.Visible) LoadAttendance();
        if (PagePeople.Visibility == Visibility.Visible) LoadPeople();
    }

    // ---------- syncing ----------

    private async void SyncButton_Click(object sender, RoutedEventArgs e) => await SyncNowAsync(manual: true);

    private void AutoToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        AutoToggle.Content = Loc.T(AutoToggle.IsChecked == true ? "Auto-sync on" : "Auto-sync off");
        ApplyTimer();
    }

    private void ApplyTimer()
    {
        _syncTimer.Interval = TimeSpan.FromSeconds(Math.Max(5, _settings.SyncIntervalSeconds));
        if (AutoToggle.IsChecked == true) _syncTimer.Start(); else _syncTimer.Stop();

        _importTimer.Stop();
        if (_settings.AutoImportFromPdks)
        {
            _importTimer.Interval = TimeSpan.FromMinutes(Math.Max(1, _settings.AutoImportMinutes));
            _importTimer.Start();
        }
    }

    // Quietly copies recent scans from the original software's database (read-only).
    private async Task AutoImportAsync()
    {
        if (_importBusy) return;
        _importBusy = true;
        try
        {
            var r = await Task.Run(() => PdksImporter.Import(_settings, _store, recentDays: 3, includeSchedule: false));
            if (r.RecordsAdded > 0)
            {
                AppServices.Log("Info", "Import", $"Copied {r.RecordsAdded} new scan(s) from the original software.");
                AppServices.RaiseDataChanged();
            }
        }
        catch (Exception ex)
        {
            AppServices.Log("Warn", "Import", "Automatic copy from the original software failed: " + ex.GetBaseException().Message);
        }
        finally { _importBusy = false; }
    }

    // The web app cannot reach the device itself; it leaves a request in the database and this app does the sync.
    private async Task AnswerWebSyncAsync()
    {
        if (_webSyncBusy) return;
        (long Id, string By)? request;
        try { request = _store.GetSyncRequest(); }
        catch (Exception) { return; }
        if (request is not { } r || r.Id == _lastWebRequest) return;

        _lastWebRequest = r.Id;
        _webSyncBusy = true;
        try
        {
            while (_busy) await Task.Delay(200);
            var before = _store.Count();
            await SyncNowAsync(manual: true);
            var error = DeviceError.Text;
            if (_settings.AutoImportFromPdks) await AutoImportAsync();
            var added = _store.Count() - before;
            AppServices.Log("Info", "Sync", $"Sync requested from the web by {r.By}: {added} new scan(s).");
            _store.SaveSyncResult(r.Id, error.Length == 0, added, error);
        }
        catch (Exception ex)
        {
            try { _store.SaveSyncResult(r.Id, false, 0, ex.GetBaseException().Message); } catch (Exception) { }
        }
        finally { _webSyncBusy = false; }
    }

    // ---------- department filter ----------

    private void LoadDepartments()
    {
        _loadingDepts = true;
        try
        {
            var items = new List<DeptItem> { new(null, Loc.T("All departments")) };
            items.AddRange(_store.GetDepartments().OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).Select(d => new DeptItem(d.Id, d.Name)));
            DeptBox.ItemsSource = items;
            DeptBox.SelectedItem = items.FirstOrDefault(i => i.Id == AppServices.DepartmentFilter) ?? items[0];
            if (DeptBox.SelectedItem is DeptItem { Id: null }) AppServices.DepartmentFilter = null;

            DeptColumn.ItemsSource = new[] { "" }.Concat(items.Skip(1).Select(i => i.Name)).ToList();
        }
        finally { _loadingDepts = false; }
    }

    private void DeptBox_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingDepts || !_ready || DeptBox.SelectedItem is not DeptItem d) return;
        AppServices.DepartmentFilter = d.Id;
        AppServices.RaiseDataChanged();
    }

    private void Departments_Click(object sender, RoutedEventArgs e)
    {
        new DepartmentsWindow { Owner = this }.ShowDialog();
        LoadDepartments();
        LoadPeople();
    }

    private async Task SyncNowAsync(bool manual)
    {
        if (_busy) return;
        _busy = true;
        SyncButton.IsEnabled = false;
        SetStatus("Syncing…", "Warn");
        try
        {
            var r = await Task.Run(() => _sync.PullOnce(_settings.AcknowledgeScans, force: manual));
            _serial = r.Serial;
            _pending = r.Pending;
            _lastSync = DateTime.Now;
            DeviceError.Text = "";
            SetStatus("Connected", "Good");

            if (r.ClockSet) AppServices.Log("Info", "Device", "Device clock set to this PC's time.");
            if (_online == false) AppServices.Log("Info", "Device", "Device reachable again.");
            _online = true;

            if (r.Added.Count > 0)
            {
                var names = _store.GetNames();
                var last = r.Added[^1];
                var who = names.TryGetValue(last.EnrollNumber, out var n) ? n : $"ID {last.EnrollNumber}";
                ShowToast(r.Added.Count == 1
                    ? $"{who} · {last.VerifyName} · {last.Timestamp:HH:mm}"
                    : Loc.F("{0} new scans, latest {1} at {2:HH:mm}", r.Added.Count, who, last.Timestamp));
                AppServices.Log("Info", "Sync", $"Collected {r.Added.Count} new scan(s) from the device" + (r.Acknowledged ? " and cleared them from its queue." : "."));
                LoadAttendance();
                LoadUnnamed();
                AppServices.RaiseDataChanged();
            }
            else if (manual)
            {
                ShowToast("Up to date. Nothing new on the device.");
            }
        }
        catch (Exception ex)
        {
            SetStatus("Offline", "Bad");
            DeviceError.Text = Loc.F("Could not reach the device: {0}", ex.GetBaseException().Message);
            if (_online != false) AppServices.Log("Warn", "Device", "Device unreachable: " + ex.GetBaseException().Message);
            _online = false;
            if (manual) ShowToast("Sync failed. Check the device and network.");
        }
        finally
        {
            _busy = false;
            SyncButton.IsEnabled = true;
            RefreshDashboard();
        }
    }

    private void SetStatus(string text, string brushKey)
    {
        StatusText.Text = Loc.T(text);
        StatusDot.Fill = (Brush)FindResource(brushKey);
        _tray.SetTooltip($"Attendance · {text}" + (_lastSync is { } t ? $" · {t:HH:mm}" : ""));
    }

    private void ShowToast(string message)
    {
        ToastText.Text = Loc.T(message);
        Toast.Visibility = Visibility.Visible;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    // ---------- dashboard ----------

    private void RefreshDashboard()
    {
        var today = DateTime.Today;
        var ctx = AppServices.Context();
        var scans = _store.GetRange(today, today.AddDays(1));
        var rows = ctx.Daily(scans);

        TodayText.Text = Loc.Dates(today.ToString("dddd, d MMMM yyyy", CultureInfo.InvariantCulture));
        PresentValue.Text = rows.Count.ToString();
        ScansValue.Text = scans.Count.ToString();
        LateValue.Text = rows.Count(r => r.LateMinutes > 0).ToString();
        EarlyValue.Text = rows.Count(r => r.EarlyLeaveMinutes > 0).ToString();
        LateHint.Text = ctx.Shifts.ShiftCount > 0 ? Loc.T("past the grace time") : Loc.F("after {0}", _settings.WorkStart.ToString(@"hh\:mm"));
        PendingValue.Text = _pending.ToString();

        var notIn = Reports.NotInToday(ctx);
        NotInValue.Text = notIn.Count.ToString();
        NotInList.ItemsSource = notIn.Select(n => new NotInRow(n.Name.Length > 0 ? n.Name : $"ID {n.Enroll}", n.Shift)).ToList();
        NotInEmpty.Text = ctx.Shifts.ShiftCount == 0
            ? Loc.T("No schedule yet. Import from the original software (Settings) or assign shifts in Schedule.")
            : notIn.Count == 0 ? Loc.T("Everyone expected today has scanned in.") : "";

        DeviceAddress.Text = $"{_settings.DeviceIp}:{_settings.DevicePort}";
        DeviceSerial.Text = _serial.Length > 0 ? _serial : "—";
        DeviceLastSync.Text = _lastSync is { } t ? t.ToString("HH:mm:ss", CultureInfo.InvariantCulture) : "—";
        SidebarSync.Text = _lastSync is { } s ? Loc.F("Last sync {0:HH:mm:ss}", s) : Loc.T("Not synced yet");

        FeedList.ItemsSource = _store.GetLatest(60).Where(r => ctx.Allows(r.EnrollNumber)).Take(12).Select(r => ToFeed(r, ctx.Names, today)).ToList();
    }

    private static FeedRow ToFeed(AttendanceRecord r, IReadOnlyDictionary<uint, string> names, DateTime today)
    {
        var name = names.TryGetValue(r.EnrollNumber, out var n) ? n : Loc.T("Unnamed");
        var time = r.Timestamp.Date == today
            ? r.Timestamp.ToString("HH:mm", CultureInfo.InvariantCulture)
            : Loc.Dates(r.Timestamp.ToString("d MMM HH:mm", CultureInfo.InvariantCulture));
        var (back, fore) = MethodColors(r);
        return new FeedRow(time, name, $"ID {r.EnrollNumber}", r.VerifyName, back, fore, name.Length > 0 ? name[..1].ToUpperInvariant() : "?");
    }

    private static (Brush, Brush) MethodColors(AttendanceRecord r) =>
        r.IsManual ? (Brush("#FEF3C7"), Brush("#B45309")) : r.VerifyCode switch
        {
            5 => (Brush("#EDE9FE"), Brush("#6D28D9")),
            1 => (Brush("#DCFCE7"), Brush("#15803D")),
            _ => (Brush("#F1F5F9"), Brush("#475569")),
        };

    private static Brush Brush(string hex) => (Brush)new BrushConverter().ConvertFromString(hex)!;

    // ---------- attendance ----------

    private void Filter_Changed(object sender, RoutedEventArgs e) { if (_ready) LoadAttendance(); }

    private void Filter_TextChanged(object sender, TextChangedEventArgs e) { if (_ready) LoadAttendance(); }

    private void LoadAttendance()
    {
        if (!_ready) return;
        var from = (FromPicker.SelectedDate ?? DateTime.Today.AddDays(-6)).Date;
        var to = (ToPicker.SelectedDate ?? DateTime.Today).Date.AddDays(1);
        var ctx = AppServices.Context();
        var names = ctx.Names;
        var q = SearchBox.Text.Trim();

        bool Match(uint id) =>
            ctx.Allows(id) && (q.Length == 0
            || id.ToString().Contains(q, StringComparison.Ordinal)
            || (names.TryGetValue(id, out var n) && n.Contains(q, StringComparison.CurrentCultureIgnoreCase)));

        bool summary = ViewSummary.IsChecked == true;
        var all = _store.GetRange(from, to, includeVoided: ShowVoided.IsChecked == true).Where(r => Match(r.EnrollNumber)).ToList();
        var counted = all.Where(r => !r.Voided).ToList();
        _scanRows = counted;
        _summaryRows = ctx.Daily(counted);

        DayGrid.Visibility = summary ? Visibility.Visible : Visibility.Collapsed;
        ScanGrid.Visibility = summary ? Visibility.Collapsed : Visibility.Visible;

        DayGrid.ItemsSource = _summaryRows.Select(r => new DayRow(
            Loc.Dates(r.Date.ToString("ddd d MMM", CultureInfo.InvariantCulture)),
            r.Enroll.ToString(),
            r.Name.Length > 0 ? r.Name : "—",
            ctx.DeptName(r.Enroll) is { Length: > 0 } dn ? dn : "—",
            r.Shift.Length > 0 ? r.Shift : "—",
            r.FirstIn.ToString("HH:mm"),
            r.LastOut?.ToString("HH:mm") ?? "—",
            r.Worked == TimeSpan.Zero ? "—" : Loc.F("{0}h {1:00}m", (int)r.Worked.TotalHours, r.Worked.Minutes),
            r.LateMinutes > 0 ? Loc.F("{0} min", r.LateMinutes) : "—",
            r.EarlyLeaveMinutes > 0 ? Loc.F("{0} min", r.EarlyLeaveMinutes) : "—",
            r.Punches.ToString(),
            r.Date, r.Enroll)).ToList();

        ScanGrid.ItemsSource = all.OrderByDescending(r => r.Timestamp).Select(r => new ScanView(
            r.Id,
            Loc.Dates(r.Timestamp.ToString("ddd d MMM HH:mm:ss", CultureInfo.InvariantCulture)),
            r.EnrollNumber.ToString(),
            names.TryGetValue(r.EnrollNumber, out var nm) ? nm : "—",
            r.VerifyName,
            r.Note ?? "",
            r.Voided ? Loc.T("Voided") : "",
            r.IsManual, r.Voided, r.Timestamp)).ToList();

        var empty = summary ? _summaryRows.Count == 0 : all.Count == 0;
        AttendanceEmpty.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        AttendanceCount.Text = summary
            ? Loc.F("{0} daily row(s) from {1} scan(s)", _summaryRows.Count, counted.Count)
            : Loc.F("{0} scan(s)", all.Count) + (all.Count(r => r.IsManual) > 0 ? Loc.F(" · {0} manual", all.Count(r => r.IsManual)) : "")
              + (all.Count(r => r.Voided) > 0 ? Loc.F(" · {0} voided", all.Count(r => r.Voided)) : "")
              + Loc.T(" · right-click a scan to void or restore it");
    }

    private void AddManual_Click(object sender, RoutedEventArgs e)
    {
        var people = _store.GetPeople().Where(p => p.Active)
            .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(p => new PersonChoice(p.Enroll, $"{p.Name} · {p.Enroll}")).ToList();

        uint? preselect = null;
        DateTime? date = null;
        if (ViewSummary.IsChecked == true && DayGrid.SelectedItem is DayRow d) { preselect = d.Enroll; date = d.DateValue; }
        else if (ScanGrid.SelectedItem is ScanView s && uint.TryParse(s.Id, out var id)) preselect = id;

        var win = new ManualEntryWindow(people, preselect, date) { Owner = this };
        if (win.ShowDialog() != true) return;

        var name = _store.GetNames().TryGetValue(win.Enroll, out var n) ? n : $"ID {win.Enroll}";
        var added = new List<string>();
        foreach (var (label, at) in new[] { ("log in", win.LogIn), ("log off", win.LogOff) })
        {
            if (at is not { } moment) continue;
            if (_store.AddManual(win.Enroll, moment, $"{label}: {win.Note}"))
            {
                added.Add($"{label} {moment:HH:mm}");
                AppServices.Log("Info", "Manual", $"Manual {label} added for {name} (ID {win.Enroll}) at {moment:yyyy-MM-dd HH:mm}: {win.Note}");
            }
        }
        if (added.Count > 0)
        {
            AppServices.RaiseDataChanged();
            ShowToast(Loc.F("Added for {0}: {1}.", name, string.Join(", ", added)));
        }
        else
        {
            ShowToast("Those entries already exist.");
        }
    }

    private void DayGrid_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DayGrid.SelectedItem is not DayRow d) return;
        _ready = false;
        FromPicker.SelectedDate = d.DateValue;
        ToPicker.SelectedDate = d.DateValue;
        SearchBox.Text = d.Id;
        ViewScans.IsChecked = true;
        _ready = true;
        LoadAttendance();
        ShowToast(Loc.F("{0}, {1}: double-click a scan to edit it, or use Add manual entry.", d.Name, d.Date));
    }

    private void ScanGrid_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ScanGrid.SelectedItem is ScanView) EditScan_Click(sender, e);
    }

    private void EditScan_Click(object sender, RoutedEventArgs e)
    {
        if (ScanGrid.SelectedItem is not ScanView s) { ShowToast("Select a scan first."); return; }
        if (s.IsVoided) { ShowToast("Restore the scan before editing it."); return; }
        if (!uint.TryParse(s.Id, out var id)) return;

        var people = _store.GetPeople().Select(p => new PersonChoice(p.Enroll, $"{p.Name} · {p.Enroll}")).ToList();
        var win = new ManualEntryWindow(people, id, s.Time, editing: true, note: s.IsManual ? s.Note : "") { Owner = this };
        if (win.ShowDialog() != true) return;
        if (win.When == s.Time && (!s.IsManual || win.Note == s.Note)) { ShowToast("Nothing changed."); return; }

        if (_store.CorrectScan(s.Key, win.When, win.Note))
        {
            AppServices.Log("Warn", "Manual", $"Scan edited: {s.Name} (ID {s.Id}) {s.Time:yyyy-MM-dd HH:mm:ss} → {win.When:yyyy-MM-dd HH:mm}. Reason: {win.Note}");
            AppServices.RaiseDataChanged();
            ShowToast(Loc.F("Scan changed to {0:HH:mm}.", win.When));
        }
        else ShowToast("An entry at that exact time already exists.");
    }

    private void VoidScan_Click(object sender, RoutedEventArgs e)
    {
        if (ScanGrid.SelectedItem is not ScanView s) { ShowToast("Select a scan first."); return; }
        if (s.IsVoided) { ShowToast("That scan is already voided."); return; }

        var prompt = new PromptWindow("Void this scan?",
            Loc.F("{0} · {1}\nIt stays in the database but no report will count it. Write the reason.", s.Name, s.When), "Void scan") { Owner = this };
        if (prompt.ShowDialog() != true) return;

        _store.SetVoided(s.Key, true, prompt.Text);
        AppServices.Log("Warn", "Manual", $"Scan voided: {s.Name} (ID {s.Id}) {s.When}. Reason: {prompt.Text}");
        AppServices.RaiseDataChanged();
        ShowToast("Scan voided.");
    }

    private void RestoreScan_Click(object sender, RoutedEventArgs e)
    {
        if (ScanGrid.SelectedItem is not ScanView s) { ShowToast("Select a scan first."); return; }
        if (!s.IsVoided) { ShowToast("That scan is not voided."); return; }
        _store.SetVoided(s.Key, false);
        AppServices.Log("Info", "Manual", $"Scan restored: {s.Name} (ID {s.Id}) {s.When}.");
        AppServices.RaiseDataChanged();
        ShowToast("Scan restored.");
    }

    private void DeleteManual_Click(object sender, RoutedEventArgs e)
    {
        if (ScanGrid.SelectedItem is not ScanView s) { ShowToast("Select an entry first."); return; }
        if (!s.IsManual) { ShowToast("Only manual entries can be deleted. Void a device scan instead."); return; }
        if (Dlg.Show(this, Loc.F("Delete the manual entry for {0} at {1}?", s.Name, s.When), "Delete manual entry", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        _store.DeleteManual(s.Key);
        AppServices.Log("Warn", "Manual", $"Manual entry deleted: {s.Name} (ID {s.Id}) {s.When}.");
        AppServices.RaiseDataChanged();
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        bool summary = ViewSummary.IsChecked == true;
        var ctx = AppServices.Context();
        var from = (FromPicker.SelectedDate ?? DateTime.Today).Date;
        var to = (ToPicker.SelectedDate ?? DateTime.Today).Date;
        var table = new ReportTable
        {
            Title = summary ? "Daily attendance" : "All scans",
            Subtitle = Loc.F("{0} to {1}", from.ToString("yyyy-MM-dd"), to.ToString("yyyy-MM-dd")),
        };

        static string Mins(int m) => m > 0 ? m.ToString() : "";
        if (summary)
        {
            table.Headers.AddRange(new[] { "Date", "ID", "Name", "Department", "Shift", "First in", "Last out", "Worked", "Late (min)", "Early leave (min)", "Scans" });
            foreach (var r in _summaryRows)
                table.Rows.Add(new[]
                {
                    r.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), r.Enroll.ToString(), r.Name, ctx.DeptName(r.Enroll), r.Shift,
                    r.FirstIn.ToString("HH:mm"), r.LastOut?.ToString("HH:mm") ?? "",
                    r.Worked == TimeSpan.Zero ? "" : $"{(int)r.Worked.TotalHours}:{r.Worked.Minutes:00}",
                    Mins(r.LateMinutes), Mins(r.EarlyLeaveMinutes), r.Punches.ToString(),
                });
        }
        else
        {
            table.Headers.AddRange(new[] { "Time", "ID", "Name", "Department", "Method", "Note" });
            foreach (var r in _scanRows.OrderBy(r => r.Timestamp))
                table.Rows.Add(new[]
                {
                    r.Timestamp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), r.EnrollNumber.ToString(),
                    ctx.Names.TryGetValue(r.EnrollNumber, out var nm) ? nm : "", ctx.DeptName(r.EnrollNumber), r.VerifyName, r.Note ?? "",
                });
        }
        ExportHelper.Save(this, Loc.Localize(table), summary ? "attendance_daily" : "attendance_scans");
    }

    // ---------- people ----------

    private void LoadPeople()
    {
        if (!_ready) return;
        _people.Clear();
        bool showFormer = ShowFormer.IsChecked == true;
        var ctx = AppServices.Context();
        foreach (var (id, name, active) in _store.GetPeople()
                     .Where(p => (showFormer || p.Active) && ctx.Allows(p.Enroll))
                     .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
            _people.Add(new PersonRow { Enroll = id, Name = name, Status = Loc.T(active ? "Active" : "Former"), Department = ctx.DeptName(id) });
        LoadUnnamed();
    }

    private void LoadUnnamed()
    {
        if (!_ready) return;
        var items = _store.GetUnnamedIds(60)
            .Select(u => new UnnamedItem(u.Enroll, Loc.F("ID {0} · {1} scan(s) · last {2}", u.Enroll, u.Scans, Loc.Dates(u.Last.ToString("d MMM HH:mm", CultureInfo.InvariantCulture)))))
            .ToList();
        UnnamedBox.ItemsSource = items;
        UnnamedBox.DisplayMemberPath = nameof(UnnamedItem.Label);
        UnnamedBox.SelectedIndex = -1;
        UnnamedBox.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        NextIdHint.Text = items.Count > 0
            ? Loc.F("{0} unnamed ID(s) · next free ID: {1}", items.Count, _store.NextFreeId())
            : Loc.F("No unnamed IDs · next free ID: {0}", _store.NextFreeId());
    }

    private void UnnamedBox_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (UnnamedBox.SelectedItem is UnnamedItem u)
        {
            NewId.Text = u.Enroll.ToString();
            NewName.Focus();
        }
    }

    private void ShowFormer_Changed(object sender, RoutedEventArgs e) => LoadPeople();

    private void PeopleGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit) return;
        if (e.Row.Item is PersonRow person && e.EditingElement is ComboBox cb)
        {
            var chosen = cb.SelectedItem as string ?? cb.Text ?? "";
            var dept = _store.GetDepartments().FirstOrDefault(d => d.Name == chosen);
            _store.SetDepartment(new[] { person.Enroll }, dept?.Id);
            AppServices.Log("Info", "People", $"{person.Name} (ID {person.Enroll}) moved to {(dept is null ? "no department" : dept.Name)}.");
            Dispatcher.BeginInvoke(() => AppServices.RaiseDataChanged());
            return;
        }
        if (e.Row.Item is PersonRow p && e.EditingElement is TextBox tb && tb.Text.Trim().Length > 0)
        {
            _store.SetName(p.Enroll, tb.Text.Trim());
            AppServices.Log("Info", "People", $"Renamed ID {p.Enroll} to “{tb.Text.Trim()}”.");
            AppServices.RaiseDataChanged();
        }
    }

    private void AddPerson_Click(object sender, RoutedEventArgs e)
    {
        if (!uint.TryParse(NewId.Text.Trim(), out var id) || NewName.Text.Trim().Length == 0)
        {
            ShowToast("Enter a numeric ID and a name.");
            return;
        }
        _store.SetName(id, NewName.Text.Trim());
        _store.SetActive(id, true);
        AppServices.Log("Info", "People", $"Added ID {id}: {NewName.Text.Trim()}.");
        NewId.Clear();
        NewName.Clear();
        AppServices.RaiseDataChanged();
    }

    private void RemovePerson_Click(object sender, RoutedEventArgs e)
    {
        if (PeopleGrid.SelectedItem is not PersonRow p) { ShowToast("Select a person first."); return; }
        _store.SetActive(p.Enroll, false);
        AppServices.Log("Info", "People", $"{p.Name} (ID {p.Enroll}) marked as former.");
        AppServices.RaiseDataChanged();
        ShowToast(Loc.F("{0} marked as former. Their old scans keep the name.", p.Name));
    }

    private void RestorePerson_Click(object sender, RoutedEventArgs e)
    {
        if (PeopleGrid.SelectedItem is not PersonRow p) { ShowToast("Select a person first."); return; }
        _store.SetActive(p.Enroll, true);
        AppServices.Log("Info", "People", $"{p.Name} (ID {p.Enroll}) is active again.");
        AppServices.RaiseDataChanged();
        ShowToast(Loc.F("{0} is active again.", p.Name));
    }

    private void ImportNames_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "CSV file (*.csv)|*.csv|All files|*.*" };
        if (dlg.ShowDialog(this) != true) return;
        var n = _store.ImportNames(dlg.FileName);
        AppServices.Log("Info", "People", $"Imported {n} name(s) from {Path.GetFileName(dlg.FileName)}.");
        AppServices.RaiseDataChanged();
        ShowToast(Loc.F("Imported {0} name(s).", n));
    }

    // ---------- settings ----------

    private void FillSettingsFields()
    {
        SetIp.Text = _settings.DeviceIp;
        SetPort.Text = _settings.DevicePort.ToString();
        SetInterval.Text = _settings.SyncIntervalSeconds.ToString();
        SetStart.Text = _settings.WorkStart.ToString(@"hh\:mm");
        SetEnd.Text = _settings.WorkEnd.ToString(@"hh\:mm");
        SetAutostart.IsChecked = IsAutostartOn();
        SetTray.IsChecked = _settings.MinimizeToTray;
        SetAck.IsChecked = _settings.AcknowledgeScans;
        SetLogDays.Text = _settings.LogKeepDays.ToString();
        SetClockAuto.IsChecked = _settings.AutoSetClock;
        SetLanguage.SelectedIndex = _settings.Language == "az" ? 1 : 0;
        SetAutoImport.IsChecked = _settings.AutoImportFromPdks;
        SetImportMinutes.Text = _settings.AutoImportMinutes.ToString();
        DataFolder.Text = Loc.F("Data folder: {0}", AppPaths.DataDir);
        RefreshSettingsInfo();
    }

    private void RefreshSettingsInfo()
    {
        if (!_ready && !IsLoaded) return;
        var latest = Maintenance.LatestBackup();
        BackupInfo.Text = latest is null
            ? Loc.T("No backup yet.")
            : Loc.F("Latest backup: {0:yyyy-MM-dd HH:mm} ({1}, {2:0.0} MB)", latest.LastWriteTime, latest.Name, latest.Length / 1048576.0);

        var size = File.Exists(AppPaths.Database) ? new FileInfo(AppPaths.Database).Length / 1048576.0 : 0;
        AboutText.Text = Loc.F("Attendance {0}. Reads attendance from the face and fingerprint terminal on your network. {1:N0} scans stored, database {2:0.0} MB.", AppVersion, _store.Count(), size);
    }

    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        if (!System.Net.IPAddress.TryParse(SetIp.Text.Trim(), out _)
            || !int.TryParse(SetPort.Text.Trim(), out var port) || port is < 1 or > 65535
            || !int.TryParse(SetInterval.Text.Trim(), out var interval) || interval < 5
            || !int.TryParse(SetLogDays.Text.Trim(), out var logDays) || logDays is < 7 or > 3650
            || !int.TryParse(SetImportMinutes.Text.Trim(), out var importMin) || importMin is < 1 or > 1440
            || !TimeSpan.TryParseExact(SetStart.Text.Trim(), @"h\:mm", CultureInfo.InvariantCulture, out var start)
            || !TimeSpan.TryParseExact(SetEnd.Text.Trim(), @"h\:mm", CultureInfo.InvariantCulture, out var end))
        {
            ShowToast("Check the values: valid IP, port, interval of 5+ seconds, log days 7+, times like 09:00.");
            return;
        }

        bool ack = SetAck.IsChecked == true;
        if (ack && !_settings.AcknowledgeScans
            && Dlg.Show(this,
                "With this on, the device forgets each scan as soon as this app has saved it, so the original software will no longer receive new scans.\n\nTurn it on?",
                "Take over from the original software", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            SetAck.IsChecked = false;
            ack = false;
        }

        _settings.DeviceIp = SetIp.Text.Trim();
        _settings.DevicePort = port;
        _settings.SyncIntervalSeconds = interval;
        _settings.WorkStart = start;
        _settings.WorkEnd = end;
        _settings.AcknowledgeScans = ack;
        _settings.MinimizeToTray = SetTray.IsChecked == true;
        _settings.LogKeepDays = logDays;
        _settings.AutoSetClock = SetClockAuto.IsChecked == true;
        _settings.AutoImportFromPdks = SetAutoImport.IsChecked == true;
        _settings.AutoImportMinutes = importMin;
        var newLanguage = (SetLanguage.SelectedItem as ComboBoxItem)?.Tag as string ?? "en";
        bool languageChanged = newLanguage != _settings.Language;
        _settings.Language = newLanguage;
        _settings.Save();

        try { SetAutostart_(SetAutostart.IsChecked == true); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            ShowToast("Saved, but Windows blocked the startup entry.");
            return;
        }

        AppServices.Log("Info", "Settings", $"Settings saved (device {_settings.DeviceIp}:{_settings.DevicePort}, every {interval}s, take-over {(ack ? "on" : "off")}).");
        ApplyTimer();
        AppServices.RaiseDataChanged();
        ShowToast("Settings saved.");
        if (languageChanged) RestartForLanguage();
    }

    private void RestartForLanguage()
    {
        Loc.Init(_settings.Language);
        if (Dlg.Show(this, "The language changes after the app restarts. Restart now?", "Language", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        var exe = Environment.ProcessPath!;
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c timeout /t 2 /nobreak >nul & start \"\" \"{exe}\"") { CreateNoWindow = true, UseShellExecute = false });
        ExitApp();
    }

    private async void SetClock_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var ok = await Task.Run(() => _sync.SetClock());
            AppServices.Log(ok ? "Info" : "Warn", "Device", ok ? "Device clock set to this PC's time." : "The device did not confirm the new time.");
            ShowToast(ok ? Loc.F("Device clock set to {0:HH:mm:ss}.", DateTime.Now) : "The device did not confirm the new time.");
        }
        catch (Exception ex)
        {
            AppServices.Log("Error", "Device", "Could not set the clock: " + ex.GetBaseException().Message);
            ShowToast(Loc.F("Could not set the clock: {0}", ex.GetBaseException().Message));
        }
    }

    private async void ImportPdks_Click(object sender, RoutedEventArgs e)
    {
        ImportButton.IsEnabled = false;
        ImportResult.Text = Loc.T("Importing…");
        try
        {
            var r = await Task.Run(() => PdksImporter.Import(_settings, _store));
            ImportResult.Text = Loc.F("Read {0:N0} scans ({1:N0} new), {2} new name(s), {3} departments, {4} shifts, {5:N0} shift days, {6} leave entries.", r.RecordsRead, r.RecordsAdded, r.NamesAdded, r.Departments, r.Shifts, r.Assignments, r.Leaves);
            AppServices.Log("Info", "Import", "Imported from the original software: " + ImportResult.Text);
            AppServices.RaiseDataChanged();
        }
        catch (Exception ex)
        {
            ImportResult.Text = Loc.F("Import failed: {0}", ex.GetBaseException().Message);
            AppServices.Log("Error", "Import", ImportResult.Text);
        }
        finally
        {
            ImportButton.IsEnabled = true;
        }
    }

    private async void BackupNow_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = await Task.Run(() => Maintenance.BackupNow(_store));
            AppServices.Log("Info", "Backup", "Backup saved: " + Path.GetFileName(path));
            RefreshSettingsInfo();
            ShowToast("Backup saved.");
        }
        catch (Exception ex)
        {
            AppServices.Log("Error", "Backup", "Backup failed: " + ex.GetBaseException().Message);
            ShowToast(Loc.F("Backup failed: {0}", ex.GetBaseException().Message));
        }
    }

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Attendance backup (*.db)|*.db", InitialDirectory = Maintenance.BackupDir };
        if (dlg.ShowDialog(this) != true) return;

        if (Dlg.Show(this,
                Loc.F("Replace everything in this app with the backup “{0}”?\n\nYour current data is saved first, and the app restarts to finish the restore.", Path.GetFileName(dlg.FileName)),
                "Restore from backup", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        try
        {
            Maintenance.StageRestore(dlg.FileName);
            AppServices.Log("Warn", "Backup", "Restore from " + Path.GetFileName(dlg.FileName) + " scheduled; restarting.");
            var exe = Environment.ProcessPath!;
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c timeout /t 2 /nobreak >nul & start \"\" \"{exe}\"") { CreateNoWindow = true, UseShellExecute = false });
            ExitApp();
        }
        catch (Exception ex)
        {
            ShowToast(Loc.F("Could not start the restore: {0}", ex.GetBaseException().Message));
        }
    }

    private void OpenBackups_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Maintenance.BackupDir}\"") { UseShellExecute = true });

    private void OpenFolder_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.DataDir}\"") { UseShellExecute = true });

    private static bool IsAutostartOn()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(AutostartName) is string;
    }

    private static void SetAutostart_(bool on)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key is null) return;
        if (on && Environment.ProcessPath is { } exe) key.SetValue(AutostartName, $"\"{exe}\" --minimized");
        else key.DeleteValue(AutostartName, throwOnMissingValue: false);
    }
}
