using System.Data;
using System.Windows;
using System.Windows.Controls;
using GozcuStandalone.Reporting;
using Microsoft.Win32;

namespace GozcuStandalone.App;

public partial class DetailsWindow : Window
{
    private const string SelCol = "✓";

    private readonly ReportTable _table;
    private readonly bool _hasId;
    private List<string[]> _shown = new();
    private HashSet<string> _selectedIds = new();

    // Set when the user double-clicks a person: the main window then opens that person's timesheet.
    public uint? OpenPersonId { get; private set; }

    public DetailsWindow(ReportTable table)
    {
        InitializeComponent();
        _table = Loc.Localize(table);
        _hasId = _table.Headers.Contains("ID") || _table.Headers.Contains(Loc.T("ID"));
        Title = _table.Title;
        TitleText.Text = _table.Title;
        SubtitleText.Text = _table.Subtitle;
        FooterText.Text = _table.Footer;
        BulkBar.Visibility = _hasId ? Visibility.Visible : Visibility.Collapsed;
        Refill();
    }

    private int IdColumnIndex => _table.Headers.IndexOf("ID") is var i && i >= 0 ? i
        : _table.Headers.IndexOf(Loc.T("ID"));

    private void Refill()
    {
        var q = FilterBox.Text.Trim();
        _shown = _table.Rows
            .Where(r => q.Length == 0 || r.Any(c => c.Contains(q, StringComparison.CurrentCultureIgnoreCase)))
            .ToList();

        var dt = new DataTable();
        if (_hasId) dt.Columns.Add(SelCol, typeof(bool));
        foreach (var h in _table.Headers) dt.Columns.Add(h, typeof(string));
        foreach (var r in _shown)
        {
            var row = dt.NewRow();
            int offset = _hasId ? 1 : 0;
            if (_hasId) row[0] = _selectedIds.Contains(r[IdColumnIndex]);
            for (int i = 0; i < r.Length; i++) row[i + offset] = r[i];
            dt.Rows.Add(row);
        }

        DetailsGrid.ItemsSource = dt.DefaultView;
        EmptyText.Visibility = _shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Filter_Changed(object sender, TextChangedEventArgs e) => Refill();

    private void Grid_AutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
    {
        if (e.PropertyName == SelCol)
        {
            e.Column.Header = "";
            e.Column.Width = 36;
            return;
        }
        e.Column.Header = e.PropertyName.ToUpperInvariant();
        e.Column.IsReadOnly = true;
        bool name = e.PropertyName == Loc.T("Name");
        e.Column.Width = name ? new DataGridLength(1, DataGridLengthUnitType.Star) : DataGridLength.Auto;
        e.Column.MinWidth = name ? 180 : 70;
    }

    private void Grid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit || e.Row.Item is not DataRowView rv) return;
        if (e.Column.Header as string != "") return;
        Dispatcher.BeginInvoke(() =>
        {
            var id = rv[IdColumnIndex + 1] as string ?? "";
            if ((bool)rv[0]) _selectedIds.Add(id); else _selectedIds.Remove(id);
        });
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var r in _shown) _selectedIds.Add(r[IdColumnIndex]);
        Refill();
    }

    private void SelectNone_Click(object sender, RoutedEventArgs e)
    {
        _selectedIds.Clear();
        Refill();
    }

    private void BulkEntry_Click(object sender, RoutedEventArgs e)
    {
        var ids = _selectedIds.Select(s => uint.TryParse(s, out var v) ? v : (uint?)null).Where(v => v is not null).Select(v => v!.Value).ToList();
        if (ids.Count == 0) { AppServices.Toast("Select one or more people first."); return; }

        var win = new ManualEntryWindow(Array.Empty<PersonChoice>(), null, null, bulkCount: ids.Count) { Owner = this };
        if (win.ShowDialog() != true) return;

        int entries = 0;
        foreach (var id in ids)
        {
            foreach (var (label, at) in new[] { ("log in", win.LogIn), ("log off", win.LogOff) })
            {
                if (at is not { } moment) continue;
                if (AppServices.Store.AddManual(id, moment, $"{label}: {win.Note}")) entries++;
            }
        }
        AppServices.Log("Info", "Manual", $"Bulk manual entry added for {ids.Count} people ({entries} scan(s) written). Reason: {win.Note}");
        AppServices.Toast(Loc.F("Added {0} scan(s) for {1} people.", entries, ids.Count));
        AppServices.RaiseDataChanged();
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
