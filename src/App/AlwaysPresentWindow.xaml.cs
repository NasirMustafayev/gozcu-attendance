using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace GozcuStandalone.App;

public sealed class AlwaysPresentRow
{
    public uint Enroll { get; set; }
    public string Name { get; set; } = "";
    public bool Enabled { get; set; }
    public string FirstIn { get; set; } = "09:00";
    public string LastOut { get; set; } = "18:00";
}

public partial class AlwaysPresentWindow : Window
{
    public AlwaysPresentWindow()
    {
        InitializeComponent();
        Reload();
    }

    private void Reload()
    {
        var existing = AppServices.Store.GetAlwaysPresent().ToDictionary(e => e.Enroll);
        Grid.ItemsSource = AppServices.Store.GetPeople()
            .Where(p => p.Active)
            .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(p =>
            {
                existing.TryGetValue(p.Enroll, out var e);
                return new AlwaysPresentRow
                {
                    Enroll = p.Enroll,
                    Name = p.Name,
                    Enabled = e?.Enabled ?? false,
                    FirstIn = (e?.FirstIn ?? new TimeSpan(9, 0, 0)).ToString(@"hh\:mm", CultureInfo.InvariantCulture),
                    LastOut = (e?.LastOut ?? new TimeSpan(18, 0, 0)).ToString(@"hh\:mm", CultureInfo.InvariantCulture),
                };
            }).ToList();
    }

    private static bool TryParseTime(string text, out TimeSpan time) =>
        TimeSpan.TryParseExact(text.Trim(), @"hh\:mm", CultureInfo.InvariantCulture, out time) ||
        TimeSpan.TryParseExact(text.Trim(), @"h\:mm", CultureInfo.InvariantCulture, out time);

    private void Grid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit) return;
        if (e.Row.Item is not AlwaysPresentRow row) return;

        // Runs after the binding has applied the edited value to the row, so row.* already reflects it.
        Dispatcher.BeginInvoke(() =>
        {
            if (!TryParseTime(row.FirstIn, out var firstIn) || !TryParseTime(row.LastOut, out var lastOut))
            {
                AppServices.Toast("Enter times as HH:mm, for example 09:00.");
                Reload();
                return;
            }

            AppServices.Store.SetAlwaysPresent(row.Enroll, row.Enabled, firstIn, lastOut);
            AppServices.Log("Info", "People", row.Enabled
                ? $"{row.Name} (ID {row.Enroll}) set to always present ({row.FirstIn}–{row.LastOut})."
                : $"{row.Name} (ID {row.Enroll}) always-present turned off.");
            AppServices.RaiseDataChanged();
        });
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
