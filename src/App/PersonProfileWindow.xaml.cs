using System.Globalization;
using System.Windows;
using GozcuStandalone.Reporting;

namespace GozcuStandalone.App;

// Quick facts about one person: how many times they were missing, late or left early this month.
public partial class PersonProfileWindow : Window
{
    private readonly uint _enroll;
    private readonly bool _ready;

    public PersonProfileWindow(uint enroll)
    {
        InitializeComponent();
        _enroll = enroll;

        var first = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        MonthBox.ItemsSource = Enumerable.Range(0, 12).Select(i => first.AddMonths(-i).ToString("yyyy-MM", CultureInfo.InvariantCulture)).ToList();
        MonthBox.SelectedIndex = 0;
        _ready = true;
        Load();
    }

    private void Month_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e) { if (_ready) Load(); }

    private void Load()
    {
        var month = DateTime.TryParseExact((MonthBox.SelectedItem as string ?? "") + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var m)
            ? m : new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var p = Reports.Profile(AppServices.Context(), _enroll, month);

        Title = Loc.F("{0} · Profile", p.Name);
        NameText.Text = p.Name;
        SubtitleText.Text = Loc.F("ID {0}", p.Enroll) + (p.Department.Length > 0 ? $" · {p.Department}" : "") + " · " + Loc.Dates(month.ToString("MMMM yyyy", CultureInfo.InvariantCulture));

        AbsentValue.Text = p.Absences.ToString();
        PresentValue.Text = p.DaysPresent.ToString();
        WorkedValue.Text = MonthlySummary.Hours(p.Worked);
        LateValue.Text = MonthlySummary.Hours(TimeSpan.FromMinutes(p.LateMinutesTotal));
        LateHint.Text = p.LateDays > 0 ? Loc.F("{0} time(s) · avg {1} min", p.LateDays, p.AverageLateMinutes) : Loc.T("time(s)");
        EarlyValue.Text = MonthlySummary.Hours(TimeSpan.FromMinutes(p.EarlyMinutesTotal));
        EarlyHint.Text = p.EarlyDays > 0 ? Loc.F("{0} time(s) · avg {1} min", p.EarlyDays, p.AverageEarlyMinutes) : Loc.T("time(s)");
        OvertimeValue.Text = p.OvertimeMinutes > 0 ? Loc.F("{0} min", p.OvertimeMinutes) : "—";
    }

    private void Timesheet_Click(object sender, RoutedEventArgs e)
    {
        if (Owner is MainWindow main) main.ShowTimesheet(_enroll);
        Close();
    }
}
