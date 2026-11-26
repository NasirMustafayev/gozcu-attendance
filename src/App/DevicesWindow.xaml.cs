using System.Windows;
using GozcuStandalone.Storage;

namespace GozcuStandalone.App;

public sealed record DeviceListRow(string Id, string Name, string Location, string Address, string Serial, string Enabled);

public partial class DevicesWindow : Window
{
    public DevicesWindow()
    {
        InitializeComponent();
        Reload();
    }

    private void Reload()
    {
        DeviceGrid.ItemsSource = AppServices.Store.GetDevices()
            .Select(d => new DeviceListRow(d.Id, d.Name, d.Location, $"{d.Ip}:{d.Port}", d.Serial.Length > 0 ? d.Serial : "—", d.Enabled ? Loc.T("Yes") : Loc.T("No")))
            .ToList();
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var win = new DeviceWindow(null) { Owner = this };
        if (win.ShowDialog() != true) return;
        AppServices.Log("Info", "Device", "Device added.");
        Reload();
        AppServices.RaiseDataChanged();
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (DeviceGrid.SelectedItem is not DeviceListRow row) { if (sender is System.Windows.Controls.Button) AppServices.Toast("Select a device first."); return; }
        var existing = AppServices.Store.GetDevice(row.Id);
        if (existing is null) return;
        var win = new DeviceWindow(existing) { Owner = this };
        if (win.ShowDialog() != true) return;
        AppServices.Log("Info", "Device", $"“{row.Name}” updated.");
        Reload();
        AppServices.RaiseDataChanged();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (DeviceGrid.SelectedItem is not DeviceListRow row) { AppServices.Toast("Select a device first."); return; }
        if (Dlg.Show(this, Loc.F("Remove “{0}”? Scans already collected from it are kept.", row.Name), "Remove device", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        AppServices.Store.DeleteDevice(row.Id);
        AppServices.Log("Warn", "Device", $"“{row.Name}” removed.");
        Reload();
        AppServices.RaiseDataChanged();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
