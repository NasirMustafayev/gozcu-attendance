using System.IO;
using GozcuStandalone.Storage;

namespace GozcuStandalone.App;

// Backups and restore. A restore is staged and applied on the next start, before the database is opened.
public static class Maintenance
{
    private const int KeepDailyBackups = 14;

    public static string BackupDir
    {
        get
        {
            var dir = Path.Combine(AppPaths.DataDir, "backups");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    private static string PendingRestore => Path.Combine(AppPaths.DataDir, "restore.pending");

    public static void ApplyPendingRestore()
    {
        if (!File.Exists(PendingRestore)) return;
        try
        {
            if (File.Exists(AppPaths.Database))
                File.Copy(AppPaths.Database, Path.Combine(BackupDir, $"before-restore-{DateTime.Now:yyyyMMdd-HHmmss}.db"), overwrite: true);
            File.Copy(PendingRestore, AppPaths.Database, overwrite: true);
            File.Delete(PendingRestore);
        }
        catch (IOException) { /* try again on the next start */ }
    }

    public static void StageRestore(string backupFile) => File.Copy(backupFile, PendingRestore, overwrite: true);

    public static string BackupNow(AttendanceStore store)
    {
        var path = Path.Combine(BackupDir, $"attendance-{DateTime.Now:yyyyMMdd-HHmmss}.db");
        store.BackupTo(path);
        return path;
    }

    // At most one automatic backup per day, keeping the newest few.
    public static string? RunDaily(AttendanceStore store)
    {
        var today = Path.Combine(BackupDir, $"daily-{DateTime.Today:yyyyMMdd}.db");
        string? made = null;
        if (!File.Exists(today))
        {
            store.BackupTo(today);
            made = today;
        }

        foreach (var old in Directory.GetFiles(BackupDir, "daily-*.db").OrderByDescending(f => f).Skip(KeepDailyBackups))
        {
            try { File.Delete(old); } catch (IOException) { }
        }
        return made;
    }

    public static FileInfo? LatestBackup() =>
        new DirectoryInfo(BackupDir).GetFiles("*.db").OrderByDescending(f => f.LastWriteTime).FirstOrDefault();
}
