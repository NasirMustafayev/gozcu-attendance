using GozcuStandalone.Reporting;
using GozcuStandalone.Storage;

namespace GozcuStandalone.App;

// Shared state for every page. Pages never talk to each other directly: they raise DataChanged
// after changing something, and every page that shows data reloads itself.
public static class AppServices
{
    public static AppSettings Settings { get; set; } = null!;
    public static AttendanceStore Store { get; set; } = null!;
    public static DeviceSync Sync { get; set; } = null!;

    public static event Action? DataChanged;
    public static event Action<string>? ToastRequested;

    public static void RaiseDataChanged() => DataChanged?.Invoke();

    public static void Toast(string message) => ToastRequested?.Invoke(message);

    public static void Log(string level, string area, string message) => Store.Log(level, area, message);

    // One department filter for the whole app: every page shows only that department's people.
    public static int? DepartmentFilter { get; set; }

    public static ReportContext Context() => ReportContext.Load(Store, Settings, DepartmentFilter);
}
