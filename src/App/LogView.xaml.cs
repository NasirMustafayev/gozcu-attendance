using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using GozcuStandalone.Reporting;
using Microsoft.Win32;

namespace GozcuStandalone.App;

public sealed record LogRow(string Time, string Level, string Area, string Message);

public partial class LogView : UserControl
{
    private readonly bool _ready;
    private List<LogRow> _rows = new();

    public LogView()
    {
        InitializeComponent();
        FromBox.SelectedDate = DateTime.Today.AddDays(-7);
        ToBox.SelectedDate = DateTime.Today;
        _ready = true;

        IsVisibleChanged += (_, e) => { if ((bool)e.NewValue) Reload(); };
        AppServices.DataChanged += () => { if (IsVisible) Reload(); };
    }

    private void Filter_Changed(object sender, RoutedEventArgs e) { if (_ready) Reload(); }
    private void Level_Changed(object sender, SelectionChangedEventArgs e) { if (_ready) Reload(); }
    private void Search_Changed(object sender, TextChangedEventArgs e) { if (_ready) Reload(); }

    public void Reload()
    {
        if (!_ready || AppServices.Store is null) return;
        var from = (FromBox.SelectedDate ?? DateTime.Today.AddDays(-7)).Date;
        var to = (ToBox.SelectedDate ?? DateTime.Today).Date.AddDays(1);
        var level = LevelBox.SelectedIndex > 0 ? (string)((ComboBoxItem)LevelBox.SelectedItem).Content : null;

        var entries = AppServices.Store.GetLog(from, to, level, SearchBox.Text.Trim(), 3000);
        _rows = entries.Select(e => new LogRow(e.Time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), e.Level, e.Area, e.Message)).ToList();
        LogGrid.ItemsSource = _rows;
        EmptyText.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CountText.Text = _rows.Count >= 3000 ? Loc.T("Showing the latest 3,000 entries. Narrow the dates to see older ones.") : Loc.F(_rows.Count == 1 ? "{0} entry." : "{0} entries.", _rows.Count).TrimEnd('.');
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var table = new ReportTable { Title = "Activity log", Headers = { "Time", "Level", "Area", "Message" } };
        foreach (var r in _rows) table.Rows.Add(new[] { r.Time, r.Level, r.Area, r.Message });
        ExportHelper.Save(Window.GetWindow(this), Loc.Localize(table), "activity_log");
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (Dlg.Show(Window.GetWindow(this), "Delete the whole activity log? Scans and reports are not affected.", "Clear log",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        AppServices.Store.ClearLog();
        AppServices.Log("Info", "Log", "Activity log cleared.");
        Reload();
    }
}
