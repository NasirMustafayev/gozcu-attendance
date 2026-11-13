using WinForms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace GozcuStandalone.App;

// Notification-area icon so the app can keep syncing while its window is closed.
public sealed class TrayIcon : IDisposable
{
    private readonly WinForms.NotifyIcon _icon;

    public TrayIcon(Action open, Action syncNow, Action exit)
    {
        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add(Loc.T("Open Attendance"), null, (_, _) => open());
        menu.Items.Add(Loc.T("Sync now"), null, (_, _) => syncNow());
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(Loc.T("Exit"), null, (_, _) => exit());

        var stream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/app.ico"))!.Stream;
        _icon = new WinForms.NotifyIcon
        {
            Icon = new Drawing.Icon(stream),
            Text = "Attendance",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _icon.DoubleClick += (_, _) => open();
    }

    public void SetTooltip(string text) => _icon.Text = text.Length > 63 ? text[..63] : text;

    public void Balloon(string title, string text) => _icon.ShowBalloonTip(4000, Loc.T(title), Loc.T(text), WinForms.ToolTipIcon.Info);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
