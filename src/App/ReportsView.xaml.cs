using System.Data;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using GozcuStandalone.Reporting;
using Microsoft.Win32;

namespace GozcuStandalone.App;

public partial class ReportsView : UserControl
{
    private static readonly string[] Kinds =
    {
        "Monthly summary", "Person timesheet", "Absences", "Late arrivals", "Early leave", "Overtime", "Manual entries and voided scans",
    };

    private readonly bool _ready;
    private ReportTable? _table;
    private List<string[]> _shown = new();

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

    // Jumps straight to one person's timesheet for the current month.
    public void ShowTimesheet(uint enroll)
    {
        LoadPeople();
        MonthBox.SelectedIndex = 0;
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
        _shown = _table.Rows
            .Where(r => q.Length == 0 || r.Any(c => c.Contains(q, StringComparison.CurrentCultureIgnoreCase)))
            .ToList();

        var dt = new DataTable();
        foreach (var h in _table.Headers) dt.Columns.Add(h, typeof(string));
        foreach (var r in _shown) dt.Rows.Add(r.Cast<object>().ToArray());

        ReportGrid.ItemsSource = dt.DefaultView;
        EmptyText.Visibility = _shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SubtitleText.Text = Loc.F("{0} · {1}", _table.Title, _table.Subtitle) + (q.Length > 0 ? Loc.F(" · {0} of {1} rows match", _shown.Count, _table.Rows.Count) : "");
        FooterText.Text = _table.Footer;
    }

    private void ReportGrid_AutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
    {
        var header = e.PropertyName;
        e.Column.Header = header.ToUpperInvariant();
        e.Column.Width = header == Loc.T("Name") || header == Loc.T("Note") ? new DataGridLength(1, DataGridLengthUnitType.Star) : DataGridLength.Auto;
        e.Column.MinWidth = header == Loc.T("Name") ? 160 : header == Loc.T("Note") ? 140 : 70;
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
