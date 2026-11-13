using System.Data;
using System.Windows;
using System.Windows.Controls;
using GozcuStandalone.Reporting;
using Microsoft.Win32;

namespace GozcuStandalone.App;

public partial class DetailsWindow : Window
{
    private readonly ReportTable _table;
    private List<string[]> _shown = new();

    // Set when the user double-clicks a person: the main window then opens that person's timesheet.
    public uint? OpenPersonId { get; private set; }

    public DetailsWindow(ReportTable table)
    {
        InitializeComponent();
        _table = Loc.Localize(table);
        Title = _table.Title;
        TitleText.Text = _table.Title;
        SubtitleText.Text = _table.Subtitle;
        FooterText.Text = _table.Footer;
        Refill();
    }

    private void Refill()
    {
        var q = FilterBox.Text.Trim();
        _shown = _table.Rows
            .Where(r => q.Length == 0 || r.Any(c => c.Contains(q, StringComparison.CurrentCultureIgnoreCase)))
            .ToList();

        var dt = new DataTable();
        foreach (var h in _table.Headers) dt.Columns.Add(h, typeof(string));
        foreach (var r in _shown) dt.Rows.Add(r.Cast<object>().ToArray());

        DetailsGrid.ItemsSource = dt.DefaultView;
        EmptyText.Visibility = _shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Filter_Changed(object sender, TextChangedEventArgs e) => Refill();

    private void Grid_AutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
    {
        e.Column.Header = e.PropertyName.ToUpperInvariant();
        bool name = e.PropertyName == Loc.T("Name");
        e.Column.Width = name ? new DataGridLength(1, DataGridLengthUnitType.Star) : DataGridLength.Auto;
        e.Column.MinWidth = name ? 180 : 70;
    }

    private void Grid_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (DetailsGrid.SelectedItem is not DataRowView row || !row.DataView.Table!.Columns.Contains("ID")) return;
        if (!uint.TryParse(row["ID"] as string, out var id)) return;
        OpenPersonId = id;
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var export = new ReportTable { Title = _table.Title, Subtitle = _table.Subtitle, Headers = _table.Headers, Rows = _shown };
        ExportHelper.Save(this, export, ExportHelper.FileName(_table.Title));
    }

    private void Print_Click(object sender, RoutedEventArgs e) => PrintHelper.Print(_table, _shown);
}
