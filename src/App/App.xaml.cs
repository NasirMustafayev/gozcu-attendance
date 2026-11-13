using System.Windows;

namespace GozcuStandalone.App;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private EventWaitHandle? _showSignal;
    private MainWindow? _main;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Loc.Init(AppSettings.Load().Language);
        LocUi.Register();

        _singleInstance = new Mutex(true, "GozcuStandalone.Attendance.SingleInstance", out var created);
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "GozcuStandalone.Attendance.Show");
        if (!created)
        {
            // Another copy is already running (perhaps in the tray): ask it to come forward instead.
            _showSignal.Set();
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            try { AppServices.Store?.Log("Error", "App", "Unexpected error: " + args.Exception.GetBaseException().Message); } catch (Exception) { }
            Dlg.Show(args.Exception.GetBaseException().Message, "Something went wrong", MessageBoxButton.OK, MessageBoxImage.Warning);
            args.Handled = true;
        };

        Maintenance.ApplyPendingRestore();

        _main = new MainWindow(e.Args.Contains("--minimized"));
        _main.Show();

        var waiter = new Thread(() =>
        {
            while (_showSignal.WaitOne())
                Dispatcher.Invoke(() => _main?.ShowFromTray());
        }) { IsBackground = true };
        waiter.Start();
    }
}
