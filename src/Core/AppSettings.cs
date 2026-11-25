using System.Text.Json;

namespace GozcuStandalone;

public static class AppPaths
{
    public static string DataDir
    {
        get
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GozcuStandalone");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string Database => Path.Combine(DataDir, "attendance.db");
    public static string SettingsFile => Path.Combine(DataDir, "settings.json");
}

public sealed class AppSettings
{
    public string Language { get; set; } = "en";
    public string DeviceIp { get; set; } = "";
    public int DevicePort { get; set; } = 5005;
    public int SyncIntervalSeconds { get; set; } = 60;
    public TimeSpan WorkStart { get; set; } = new(9, 0, 0);
    public TimeSpan WorkEnd { get; set; } = new(18, 0, 0);
    public string PdksServer { get; set; } = @".\GOZCU";
    public string PdksDatabase { get; set; } = "GozcuPDKS";

    // Off by default so the original software keeps seeing the same scans.
    public bool AcknowledgeScans { get; set; }

    // Keeps this app in step with the original software's database (read-only), for scans it collected first.
    public bool AutoImportFromPdks { get; set; }
    public int AutoImportMinutes { get; set; } = 10;

    // Sets the terminal's clock to this PC once a day, as the original software does on every connection.
    public bool AutoSetClock { get; set; } = true;
    public DateTime? LastClockSet { get; set; }

    public bool MinimizeToTray { get; set; } = true;
    public bool TrayHintShown { get; set; }
    public int LogKeepDays { get; set; } = 90;

    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    // The web app passes the settings file that sits next to the database it was pointed at.
    public static AppSettings Load(string? path = null)
    {
        path ??= AppPaths.SettingsFile;
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new();
        }
        catch (JsonException) { }
        return new();
    }

    public void Save() => File.WriteAllText(AppPaths.SettingsFile, JsonSerializer.Serialize(this, Json));
}
