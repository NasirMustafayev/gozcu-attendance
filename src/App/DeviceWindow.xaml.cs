using System.Net;
using System.Windows;

namespace GozcuStandalone.App;

// Add or edit one device (face/fingerprint terminal). Null existing means "add".
public partial class DeviceWindow : Window
{
    private readonly string _id;

    public DeviceWindow(Storage.DeviceConfig? existing)
    {
        InitializeComponent();
        _id = existing?.Id ?? Guid.NewGuid().ToString("N")[..12];
        if (existing is not null)
        {
            HeadText.Text = Loc.T("Edit device");
            NameBox.Text = existing.Name;
            LocationBox.Text = existing.Location;
            IpBox.Text = existing.Ip;
            PortBox.Text = existing.Port.ToString();
            EnabledBox.IsChecked = existing.Enabled;
        }
        else
        {
            PortBox.Text = "5005";
        }
        Loaded += (_, _) => NameBox.Focus();
    }

    private void Test_Click(object sender, RoutedEventArgs e)
    {
        if (!IPAddress.TryParse(IpBox.Text.Trim(), out _) || !int.TryParse(PortBox.Text.Trim(), out var port) || port is < 1 or > 65535)
        {
            TestResult.Foreground = (System.Windows.Media.Brush)FindResource("Bad");
            TestResult.Text = Loc.T("Enter a valid IP address and port first.");
            return;
        }
        TestButton.IsEnabled = false;
        TestResult.Foreground = (System.Windows.Media.Brush)FindResource("Muted");
        TestResult.Text = Loc.T("Connecting…");
        var ip = IpBox.Text.Trim();
        Task.Run(() =>
        {
            try
            {
                var (serial, pending) = DeviceSync.TestConnect(ip, port);
                Dispatcher.Invoke(() =>
                {
                    TestResult.Foreground = (System.Windows.Media.Brush)FindResource("Good");
                    TestResult.Text = Loc.F("Connected. Serial {0}, {1} scan(s) pending.", serial, pending);
                });
            }
            catch (Exception ex)
            {
                var msg = ex.GetBaseException().Message;
                Dispatcher.Invoke(() =>
                {
                    TestResult.Foreground = (System.Windows.Media.Brush)FindResource("Bad");
                    TestResult.Text = Loc.F("Could not connect: {0}", msg);
                });
            }
            finally { Dispatcher.Invoke(() => TestButton.IsEnabled = true); }
        });
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        if (name.Length == 0) { ErrorText.Text = Loc.T("Give the device a name."); return; }
        if (!IPAddress.TryParse(IpBox.Text.Trim(), out _)) { ErrorText.Text = Loc.T("Enter a valid IP address."); return; }
        if (!int.TryParse(PortBox.Text.Trim(), out var port) || port is < 1 or > 65535) { ErrorText.Text = Loc.T("Enter a valid port (1-65535)."); return; }

        AppServices.Store.SaveDevice(_id, name, LocationBox.Text.Trim(), IpBox.Text.Trim(), port, EnabledBox.IsChecked == true);
        DialogResult = true;
    }
}
