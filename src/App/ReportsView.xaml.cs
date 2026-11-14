using System.Data;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GozcuStandalone.Reporting;
using Microsoft.Win32;

namespace GozcuStandalone.App;

public partial class ReportsView : UserControl
{
    private static readonly string[] Kinds =
    {
        "Monthly summary", "Person timesheet", "Absences", "Late arrivals", "Early leave", "Overtime", "Manual entries and voided scans",
    };

    private const string RowKindColumn = "__RowKind";
    private const string MarkFullColumn = "__MarkFull";
    private const string EditKeyColumn = "__EditKey";

    private readonly bool _ready;
    private ReportTable? _table;
    private List<string[]> _shown = new();
    private List<(string[] Row, ReportRowKind Kind, string? MarkFullKey, string? EditKey)> _shownKinds = new();

    public ReportsView()
    {
        InitializeComponent();
        KindBox.ItemsSource = Kinds.Select(Loc.T).ToList();
        KindBox.SelectedIndex = 0;

        var first = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        MonthBox.ItemsSource = Enumerable.Range(0, 36).Select(i => first.AddMonths(-i).ToString("yyyy-MM", CultureInfo.InvariantCulture)).ToList();
        MonthBox.SelectedIndex = 0;

        FromBox.SelectedDate = DateTime.Today.AddDays(-29);
        ToBox.SelectedDate = DateTime.Today;
        _ready = true;
        UpdateParameterVisibility();

        IsVisibleChanged += (_, e) => { if ((bool)e.NewValue) { LoadPeople(); Run(); } };
        AppServices.DataChanged += () => { if (IsVisible) { LoadPeople(); Run(); } };
    }

    private void LoadPeople()
    {
        var keep = (PersonBox.SelectedItem as PersonChoice)?.Enroll;
        var people = AppServices.Store.GetPeople().Where(p => p.Active)
            .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(p => new PersonChoice(p.Enroll, $"{p.Name} · {p.Enroll}")).ToList();
        PersonBox.ItemsSource = people;
        PersonBox.SelectedItem = people.FirstOrDefault(p => p.Enroll == keep) ?? people.FirstOrDefault();
    }

    // Jumps straight to one person's timesheet, for the given month or the current one.
    public void ShowTimesheet(uint enroll, DateTime? month = null)
    {
        LoadPeople();
        var wanted = (month ?? DateTime.Today).ToString("yyyy-MM", CultureInfo.InvariantCulture);
        MonthBox.SelectedIndex = MonthBox.ItemsSource is List<string> months && months.IndexOf(wanted) is var i && i >= 0 ? i : 0;
        if (PersonBox.ItemsSource is List<PersonChoice> list && list.FirstOrDefault(p => p.Enroll == enroll) is { } person)
            PersonBox.SelectedItem = person;
        KindBox.SelectedIndex = 1;
        Run();
    }

    private int Kind => KindBox.SelectedIndex;

    private void UpdateParameterVisibility()
    {
        MonthPanel.Visibility = Kind is 0 or 1 ? Visibility.Visible : Visibility.Collapsed;
        PersonPanel.Visibility = Kind == 1 ? Visibility.Visible : Visibility.Collapsed;
        RangePanel.Visibility = Kind >= 2 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Kind_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        UpdateParameterVisibility();
        Run();
    }

    private void Param_Changed(object sender, SelectionChangedEventArgs e) { if (_ready) Run(); }
    private void Date_Changed(object sender, SelectionChangedEventArgs e) { if (_ready) Run(); }
    private void Search_Changed(object sender, TextChangedEventArgs e) { if (_ready) Show(); }

    private void ThisWeek_Click(object sender, RoutedEventArgs e)
    {
        var start = DateTime.Today.AddDays(-(((int)DateTime.Today.DayOfWeek + 6) % 7));
        FromBox.SelectedDate = start;
        ToBox.SelectedDate = DateTime.Today;
    }

    private void ThisMonth_Click(object sender, RoutedEventArgs e)
    {
        FromBox.SelectedDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        ToBox.SelectedDate = DateTime.Today;
    }

    private void Run()
    {
        if (!_ready || AppServices.Store is null) return;
        try
        {
            var ctx = AppServices.Context();
            var month = DateTime.TryParseExact((MonthBox.SelectedItem as string ?? "") + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var m)
                ? m : new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            var from = (FromBox.SelectedDate ?? DateTime.Today.AddDays(-29)).Date;
            var to = (ToBox.SelectedDate ?? DateTime.Today).Date;
            if (to < from) (from, to) = (to, from);

            _table = Kind switch
            {
                0 => Reports.Monthly(ctx, month),
                1 when PersonBox.SelectedItem is PersonChoice p => Reports.Timesheet(ctx, p.Enroll, month),
                1 => new ReportTable { Title = "Person timesheet", Subtitle = "Choose a person.", Headers = { "Date" } },
                2 => Reports.Absent(ctx, from, to),
                3 => Reports.Late(ctx, from, to),
                4 => Reports.EarlyLeave(ctx, from, to),
                5 => Reports.Overtime(ctx, from, to),
                _ => Reports.Corrections(ctx, from, to),
            };
            _table = Loc.Localize(_table);

            if (Kind == 1 && PersonBox.SelectedItem is PersonChoice person)
            {
                var p = Reports.Profile(ctx, person.Enroll, month);
                PcAbsentValue.Text = p.Absences.ToString();
                PcPresentValue.Text = p.DaysPresent.ToString();
                PcWorkedValue.Text = MonthlySummary.Hours(p.Worked);
                PcLateValue.Text = MonthlySummary.Hours(TimeSpan.FromMinutes(p.LateMinutesTotal));
                PcLateHint.Text = p.LateDays > 0 ? Loc.F("{0} time(s) · avg {1} min", p.LateDays, p.AverageLateMinutes) : Loc.T("time(s)");
                PcEarlyValue.Text = MonthlySummary.Hours(TimeSpan.FromMinutes(p.EarlyMinutesTotal));
                PcEarlyHint.Text = p.EarlyDays > 0 ? Loc.F("{0} time(s) · avg {1} min", p.EarlyDays, p.AverageEarlyMinutes) : Loc.T("time(s)");
                PcOvertimeValue.Text = p.OvertimeMinutes > 0 ? Loc.F("{0} min", p.OvertimeMinutes) : "—";
                ProfileCards.Visibility = Visibility.Visible;
            }
            else
            {
                ProfileCards.Visibility = Visibility.Collapsed;
            }

            Show();
        }
        catch (Exception ex)
        {
            AppServices.Log("Error", "Reports", "Report failed: " + ex.GetBaseException().Message);
            AppServices.Toast(Loc.F("Could not build the report: {0}", ex.GetBaseException().Message));
        }
    }

    private void Show()
    {
        if (_table is null) return;
        var q = SearchBox.Text.Trim();
        _shownKinds = _table.Rows
            .Select((r, i) => (Row: r, Kind: _table.KindOf(i), MarkFullKey: _table.MarkFullKeyOf(i), EditKey: _table.EditKeyOf(i)))
            .Where(x => q.Length == 0 || x.Row.Any(c => c.Contains(q, StringComparison.CurrentCultureIgnoreCase)))
            .ToList();
        _shown = _shownKinds.Select(x => x.Row).ToList();

        var dt = new DataTable();
        foreach (var h in _table.Headers) dt.Columns.Add(h, typeof(string));
        dt.Columns.Add(RowKindColumn, typeof(ReportRowKind));
        dt.Columns.Add(MarkFullColumn, typeof(string));
        dt.Columns.Add(EditKeyColumn, typeof(string));
        foreach (var (r, kind, markKey, editKey) in _shownKinds)
        {
            var row = dt.Rows.Add();
            for (var i = 0; i < r.Length; i++) row[i] = r[i];
            row[RowKindColumn] = kind;
            row[MarkFullColumn] = (object?)markKey ?? DBNull.Value;
            row[EditKeyColumn] = (object?)editKey ?? DBNull.Value;
        }

        ReportGrid.ItemsSource = dt.DefaultView;
        EmptyText.Visibility = _shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SubtitleText.Text = Loc.F("{0} · {1}", _table.Title, _table.Subtitle) + (q.Length > 0 ? Loc.F(" · {0} of {1} rows match", _shown.Count, _table.Rows.Count) : "");
        FooterText.Text = _table.Footer;
    }

    private void ReportGrid_AutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
    {
        if (e.PropertyName is RowKindColumn or MarkFullColumn or EditKeyColumn) { e.Cancel = true; return; }
        var header = e.PropertyName;
        e.Column.Header = header.ToUpperInvariant();
        e.Column.Width = header == Loc.T("Name") || header == Loc.T("Note") ? new DataGridLength(1, DataGridLengthUnitType.Star) : DataGridLength.Auto;
        e.Column.MinWidth = header == Loc.T("Name") ? 160 : header == Loc.T("Note") ? 140 : 70;
    }

    // Tints a row that marks a missing day, so it stands out the same way as in the web app.
    // DataTable stores an enum column as its underlying int, so read it back as int, not via "as ReportRowKind?".
    private void ReportGrid_LoadingRow(object sender, DataGridRowEventArgs e)
    {
        var raw = (e.Row.Item as DataRowView)?[RowKindColumn];
        var kind = raw is int i ? (ReportRowKind)i : ReportRowKind.Normal;
        e.Row.Background = kind == ReportRowKind.Missing
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFE, 0xF2, 0xF2))
            : System.Windows.Media.Brushes.Transparent;
    }

    private void ReportGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var row = ReportGrid.SelectedItem as DataRowView;
        MarkFullButton.IsEnabled = !string.IsNullOrEmpty(row?[MarkFullColumn] as string);
        EditDayButton.IsEnabled = !string.IsNullOrEmpty(row?[EditKeyColumn] as string);
    }

    private void EditDay_Click(object sender, RoutedEventArgs e)
    {
        if ((ReportGrid.SelectedItem as DataRowView)?[EditKeyColumn] is not string key) return;
        var parts = key.Split('\t');
        if (parts.Length != 2 || !uint.TryParse(parts[0], out var enroll) || !DateTime.TryParse(parts[1], out var date)) return;
        (Window.GetWindow(this) as MainWindow)?.ShowScansFor(enroll, date);
    }

    // Opens the clicked person's timesheet, the same jump the dashboard's "today" cards use.
    private void ReportGrid_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_table is null || ReportGrid.SelectedItem is not DataRowView row) return;
        var idCol = _table.Headers.IndexOf("ID");
        if (idCol < 0 || idCol >= row.Row.ItemArray.Length) return;
        if (row[idCol] is not string s || !uint.TryParse(s, out var enroll)) return;

        var dateCol = _table.Headers.IndexOf("Date");
        DateTime? month = dateCol >= 0 && row[dateCol] is string d && DateTime.TryParse(d, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed : null;
        ShowTimesheet(enroll, month);
    }

    private void MarkFull_Click(object sender, RoutedEventArgs e)
    {
        if ((ReportGrid.SelectedItem as DataRowView)?[MarkFullColumn] is not string key) return;
        var parts = key.Split('\t');
        if (parts.Length != 2 || !uint.TryParse(parts[0], out var enroll) || !DateTime.TryParse(parts[1], out var date)) return;

        var ctx = AppServices.Context();
        var shift = ctx.Shifts.For(date, enroll);
        var name = ctx.NameOf(enroll);
        if (shift is null) { AppServices.Toast(Loc.T("That person has no shift on that day.")); return; }

        if (!AppServices.Store.MarkFull(enroll, date, shift, Environment.UserName))
        {
            AppServices.Toast(Loc.T("That day already has entries."));
            return;
        }
        AppServices.Log("Info", "Manual", $"Marked {date:yyyy-MM-dd} as a full day for {name} (ID {enroll}): {shift.Start:hh\\:mm}–{shift.End:hh\\:mm}.");
        AppServices.Toast(Loc.F("Marked {0:yyyy-MM-dd} as a full day for {1}.", date, name));
        AppServices.RaiseDataChanged();
        Run();
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_table is null) return;
        var export = new ReportTable { Title = _table.Title, Subtitle = _table.Subtitle, Headers = _table.Headers, Rows = _shown };
        ExportHelper.Save(Window.GetWindow(this), export, ExportHelper.FileName(_table.Title));
    }

    private void Print_Click(object sender, RoutedEventArgs e)
    {
        if (_table is null) return;
        PrintHelper.Print(_table, _shown);
    }
}
