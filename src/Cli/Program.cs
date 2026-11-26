using GozcuStandalone.Reporting;
using GozcuStandalone.Storage;

namespace GozcuStandalone.Cli;

public static class Program
{
    public static int Main(string[] args)
    {
        var settings = AppSettings.Load();
        var store = new AttendanceStore(AppPaths.Database);
        var sync = new DeviceSync(settings, store);
        var trace = args.Contains("--trace");
        var ack = args.Contains("--ack");
        store.MigrateLegacyDevice(settings.DeviceIp, settings.DevicePort);

        switch (args.FirstOrDefault())
        {
            case "devices":
                return Devices(args, store);

            case "status":
            {
                foreach (var d in SelectedDevices(args, store))
                {
                    try
                    {
                        var (serial, pending) = GozcuStandalone.DeviceSync.TestConnect(d.Ip, d.Port, trace);
                        Console.WriteLine($"{d.Name} ({d.Ip}:{d.Port})\n  Serial : {serial}\n  Pending: {pending}");
                    }
                    catch (Exception ex) { Console.WriteLine($"{d.Name} ({d.Ip}:{d.Port})\n  Error  : {ex.GetBaseException().Message}"); }
                }
                return 0;
            }

            case "raw":
            {
                var device = SelectedDevices(args, store).FirstOrDefault() ?? throw new InvalidOperationException("No device configured. Use `gozcu devices add`.");
                using var dev = new GozcuStandalone.Device.DeviceClient(device.Ip, device.Port) { Trace = true };
                dev.Connect();
                dev.Handshake();
                foreach (var spec in args.Skip(1).Where(a => !a.StartsWith("--")))
                {
                    var p = spec.Split(':');
                    dev.Exchange(GozcuStandalone.Device.Protocol.Request(Convert.ToUInt16(p[0], 16), p.Length > 1 ? Convert.ToByte(p[1], 16) : (byte)0, p.Length > 2 ? Convert.ToByte(p[2], 16) : (byte)0), 60);
                }
                dev.Unlock();
                return 0;
            }

            case "reset":
            {
                var device = SelectedDevices(args, store).FirstOrDefault() ?? throw new InvalidOperationException("No device configured. Use `gozcu devices add`.");
                using var dev = new GozcuStandalone.Device.DeviceClient(device.Ip, device.Port) { Trace = true };
                dev.Connect();
                dev.Handshake();
                var got = dev.FetchPending(dev.GetPendingCount()); Console.WriteLine("fetched " + got.Count);
                dev.Unlock();
                Console.WriteLine("Sent abort + unlock.");
                return 0;
            }

            case "pull":
                foreach (var d in SelectedDevices(args, store)) PullOnce(sync, store, d.Id, ack, trace);
                return 0;

            case "sync":
                return Sync(sync, store, args, ack, trace, IntervalSeconds(args, settings));

            case "report":
            {
                var days = ArgInt(args, "--days", 7);
                var from = DateTime.Today.AddDays(1 - days);
                var rows = DailyReport.Build(store.GetRange(from, DateTime.Today.AddDays(1)), store.GetNames(), settings.WorkStart, settings.WorkEnd, store.LoadShiftBook());
                var path = args.Length > 1 && !args[1].StartsWith("--") ? args[1] : null;
                if (path is not null)
                {
                    DailyReport.WriteCsv(path, rows);
                    Console.WriteLine($"Wrote {rows.Count} row(s) to {path}");
                }
                else
                {
                    foreach (var r in rows)
                        Console.WriteLine($"{r.Date:yyyy-MM-dd} #{r.Enroll,-4} {r.Name,-26} in {r.FirstIn:HH:mm} out {(r.LastOut?.ToString("HH:mm") ?? "--:--")}  late {r.LateMinutes,3}m  scans {r.Punches}");
                }
                return 0;
            }

            case "reports":
            {
                var ctx = ReportContext.Load(store, settings);
                var kind = args.Length > 1 ? args[1] : "";
                var days = ArgInt(args, "--days", 30);
                var to = DateTime.Today;
                var from = to.AddDays(1 - days);
                var month = args.Contains("--month") ? DateTime.ParseExact(args[Array.IndexOf(args, "--month") + 1] + "-01", "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) : new DateTime(to.Year, to.Month, 1);
                ReportTable? table = kind switch
                {
                    "monthly" => Reports.Monthly(ctx, month),
                    "absent" => Reports.Absent(ctx, from, to),
                    "late" => Reports.Late(ctx, from, to),
                    "early" => Reports.EarlyLeave(ctx, from, to),
                    "overtime" => Reports.Overtime(ctx, from, to),
                    "corrections" => Reports.Corrections(ctx, from, to),
                    "timesheet" when args.Length > 2 && uint.TryParse(args[2], out var id) => Reports.Timesheet(ctx, id, month),
                    _ => null,
                };
                if (table is null)
                {
                    Console.WriteLine("reports <monthly|absent|late|early|overtime|corrections|timesheet <id>> [--days N] [--month yyyy-MM] [--csv file]");
                    return 1;
                }
                var outFlag = args.Contains("--out") ? "--out" : args.Contains("--csv") ? "--csv" : null;
                if (outFlag is not null)
                {
                    var file = args[Array.IndexOf(args, outFlag) + 1];
                    table.Save(file);
                    Console.WriteLine($"Wrote {table.Rows.Count} row(s) to {file}.");
                    return 0;
                }
                Console.WriteLine($"{table.Title} - {table.Subtitle}");
                Console.WriteLine(string.Join(" | ", table.Headers));
                foreach (var row in table.Rows.Take(ArgInt(args, "--top", 25))) Console.WriteLine(string.Join(" | ", row));
                Console.WriteLine(table.Footer);
                return 0;
            }

            case "manual":
            {
                if (args.Length > 3 && args[1] == "add" && uint.TryParse(args[2], out var enroll)
                    && DateTime.TryParseExact(args[3], "yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var when))
                {
                    var ok = store.AddManual(enroll, when, args.Length > 4 ? string.Join(' ', args.Skip(4).Where(a => !a.StartsWith("--"))) : null);
                    store.Log("Info", "Manual", $"Added manual entry for ID {enroll} at {when:yyyy-MM-dd HH:mm} (command line).");
                    Console.WriteLine(ok ? "Added." : "That entry already exists.");
                    return 0;
                }
                if (args.Length > 1 && args[1] == "list")
                {
                    foreach (var (r, by, at) in store.GetCorrections(DateTime.Today.AddYears(-1), DateTime.Today.AddDays(1)))
                        Console.WriteLine($"#{r.Id,-7} ID {r.EnrollNumber,-4} {r.Timestamp:yyyy-MM-dd HH:mm}  {(r.IsManual ? "manual" : "device")}{(r.Voided ? " VOIDED" : "")}  {r.Note}  [{by} {at}]");
                    return 0;
                }
                if (args.Length > 2 && (args[1] == "void" || args[1] == "restore") && long.TryParse(args[2], out var scanId))
                {
                    store.SetVoided(scanId, args[1] == "void", args[1] == "void" ? string.Join(' ', args.Skip(3)) : null);
                    Console.WriteLine(args[1] == "void" ? "Voided." : "Restored.");
                    return 0;
                }
                if (args.Length > 4 && args[1] == "edit" && long.TryParse(args[2], out var editId)
                    && DateTime.TryParseExact(args[3], "yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var newWhen))
                {
                    var ok = store.CorrectScan(editId, newWhen, string.Join(' ', args.Skip(4)));
                    if (ok) store.Log("Warn", "Manual", $"Scan #{editId} edited to {newWhen:yyyy-MM-dd HH:mm} (command line).");
                    Console.WriteLine(ok ? "Edited." : "Not found, or an entry at that time already exists.");
                    return 0;
                }
                if (args.Length > 2 && args[1] == "delete" && long.TryParse(args[2], out var delId))
                {
                    Console.WriteLine(store.DeleteManual(delId) ? "Deleted." : "No manual entry with that id.");
                    return 0;
                }
                Console.WriteLine("manual add <enroll> \"yyyy-MM-dd HH:mm\" [note...]   |   manual delete <scan id>");
                return 1;
            }

            case "log":
                foreach (var e in store.GetLog(DateTime.Today.AddDays(-30), DateTime.Today.AddDays(1), null, null, ArgInt(args, "--top", 20)))
                    Console.WriteLine($"{e.Time:yyyy-MM-dd HH:mm:ss} {e.Level,-5} {e.Area,-8} {e.Message}");
                return 0;

            case "leave":
            {
                if (args.Length > 4 && args[1] == "add" && uint.TryParse(args[2], out var who)
                    && DateTime.TryParseExact(args[3], "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var lf)
                    && DateTime.TryParseExact(args[4], "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var lt))
                {
                    var text = args.Length > 5 ? string.Join(' ', args.Skip(5)) : "Annual leave";
                    var id = store.AddLeave(who, lf, lt, text, 10);
                    store.Log("Info", "Leave", $"Leave added for ID {who}: {lf:yyyy-MM-dd} to {lt:yyyy-MM-dd} ({text}) (command line).");
                    Console.WriteLine($"Added leave #{id}.");
                    return 0;
                }
                if (args.Length > 2 && args[1] == "delete" && long.TryParse(args[2], out var leaveId))
                {
                    Console.WriteLine(store.DeleteLeave(leaveId) ? "Deleted." : "Only leave added here can be deleted.");
                    return 0;
                }
                if (args.Length > 1 && args[1] == "list")
                {
                    var names = store.GetNames();
                    foreach (var l in store.GetLeaves(DateTime.Today.AddDays(-30), DateTime.Today.AddDays(60)).Take(ArgInt(args, "--top", 15)))
                        Console.WriteLine($"#{l.Id,-6} ID {l.Enroll,-4} {names.GetValueOrDefault(l.Enroll, ""),-24} {l.Start:yyyy-MM-dd} to {l.End:yyyy-MM-dd}  {l.Description}  [{l.Source}]");
                    return 0;
                }
                Console.WriteLine("leave add <enroll> <yyyy-MM-dd> <yyyy-MM-dd> [text]  |  leave delete <id>  |  leave list");
                return 1;
            }

            case "departments":
            {
                var map = store.GetPersonDepartments();
                foreach (var d in store.GetDepartments())
                    Console.WriteLine($"{d.Id,5}  {d.Name,-20} {map.Count(p => p.Value == d.Id)} people");
                Console.WriteLine($"{store.GetPeople().Count(p => p.Active && !map.ContainsKey(p.Enroll))} active people have no department.");
                return 0;
            }

            case "selftest":
            {
                var failures = SelfTest.Run();
                foreach (var f in failures) Console.WriteLine("FAIL " + f);
                Console.WriteLine(failures.Count == 0 ? "All packet checks match the captured traffic." : $"{failures.Count} check(s) failed.");
                return failures.Count == 0 ? 0 : 1;
            }

            case "set-time":
                foreach (var d in SelectedDevices(args, store))
                    Console.WriteLine(sync.SetClock(d.Id, trace: trace) ? $"{d.Name}: clock set to {DateTime.Now:HH:mm:ss}." : $"{d.Name}: did not confirm the new time.");
                return 0;

            case "summary":
            {
                var month = args.Contains("--month") ? args[Array.IndexOf(args, "--month") + 1] : DateTime.Today.ToString("yyyy-MM");
                if (!DateTime.TryParseExact(month + "-01", "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var first))
                {
                    Console.WriteLine("Use --month yyyy-MM");
                    return 1;
                }
                var days = DailyReport.Build(store.GetRange(first, first.AddMonths(1)), store.GetNames(), settings.WorkStart, settings.WorkEnd, store.LoadShiftBook());
                var rows = MonthlySummary.Build(days);
                foreach (var r in rows)
                    Console.WriteLine($"#{r.Enroll,-4} {r.Name,-26} days {r.DaysPresent,2}  worked {MonthlySummary.Hours(r.Worked),9}  late {r.LateDays,2}d/{r.LateMinutes,4}m  overtime {r.OvertimeMinutes,4}m  no-out {r.IncompleteDays}");
                return 0;
            }

            case "export":
            {
                var path = args.Length > 1 && !args[1].StartsWith("--") ? args[1] : "attendance_export.csv";
                var rows = store.GetAll();
                AttendanceReport.WriteCsv(path, rows, store.GetNames());
                Console.WriteLine($"Exported {rows.Count} record(s) to {path}");
                return 0;
            }

            case "import-pdks":
            {
                var r = PdksImporter.Import(settings, store);
                Console.WriteLine($"Read {r.RecordsRead} scan(s) from PDKS: {r.RecordsAdded} new, {r.NamesAdded} new name(s), {r.Shifts} shift(s), {r.Assignments} assignment(s), {r.Leaves} leave(s).");
                return 0;
            }

            case "db-check":
            {
                // Read-only integrity check of a database file (the app's own one by default).
                var file = args.Length > 1 ? args[1] : AppPaths.Database;
                using var conn = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = file, Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "PRAGMA integrity_check(50);";
                using var rd = cmd.ExecuteReader();
                while (rd.Read()) Console.WriteLine(rd.GetString(0));
                return 0;
            }

            case "request-sync":
            {
                // Asks the running desktop app to sync, the same way the web app does, and waits for its answer.
                var id = store.RequestSync("command line");
                for (int i = 0; i < 120; i++)
                {
                    Thread.Sleep(500);
                    if (store.GetSyncResult(id) is { } r)
                    {
                        Console.WriteLine(r.Ok ? $"Synced by the desktop app: {r.Added} new scan(s)." : $"Sync failed: {r.Message}");
                        return r.Ok ? 0 : 1;
                    }
                }
                Console.WriteLine("The desktop app did not answer within 60 seconds.");
                return 1;
            }

            case "web-user":
            {
                // web-user list | web-user password <name> <new password> | web-user delete <name>
                if (args.Length > 3 && args[1] == "password" && store.GetWebUser(args[2]) is { } u)
                {
                    store.SaveWebUser(u.Username, u.DisplayName, u.Role, u.Language, args[3], u.Pages);
                    store.Log("Warn", "Web", $"Web password for “{u.Username}” reset (command line).");
                    Console.WriteLine("Password changed.");
                    return 0;
                }
                if (args.Length > 2 && args[1] == "delete")
                {
                    store.DeleteWebUser(args[2]);
                    store.Log("Warn", "Web", $"Web user “{args[2]}” removed (command line).");
                    Console.WriteLine("Removed.");
                    return 0;
                }
                foreach (var w in store.GetWebUsers()) Console.WriteLine($"{w.Username,-20} {w.Role,-8} {w.DisplayName}");
                return 0;
            }

            case "names":
                return Names(args, store);

            default:
                Console.WriteLine("""
                    Usage: gozcu <command> [options]
                      devices list | devices add <name> <ip> [port] | devices remove <id>
                      status [--device id]      serial and pending record count, every device unless --device
                      pull [--device id]        fetch pending records once
                      sync [--interval N] [--device id]  keep fetching every N seconds, Ctrl+C to stop
                      report [file.csv] [--days N]  daily in/out and hours (default last 7 days)
                      export [file.csv]          every stored scan, with names
                      import-pdks                one-time, read-only copy of PDKS history and names
                      names import <file.csv> | names set <enroll> <name> | names list
                    Options: --trace (raw bytes)   --ack (remove fetched records from the device queue)
                    Reads are non-destructive unless --ack is given.
                    """);
                return 1;
        }
    }

    private static void PullOnce(DeviceSync sync, AttendanceStore store, string deviceId, bool ack, bool trace)
    {
        var result = sync.PullOnce(deviceId, ack, trace);
        if (result is null) { Console.WriteLine($"{Now()} unknown device {deviceId}."); return; }
        if (!result.Ok || result.Result is null)
        {
            Console.WriteLine($"{Now()} {result.DeviceName}: failed ({result.Error}).");
            return;
        }
        var r = result.Result;
        if (r.Pending == 0)
        {
            Console.WriteLine($"{Now()} {result.DeviceName} ({r.Serial}): nothing pending.");
            return;
        }
        var names = store.GetNames();
        Console.WriteLine($"{Now()} {result.DeviceName} ({r.Serial}): {r.Pending} pending, {r.Added.Count} new.");
        foreach (var a in r.Added)
        {
            names.TryGetValue(a.EnrollNumber, out var name);
            Console.WriteLine($"  {a.Timestamp:yyyy-MM-dd HH:mm:ss}  #{a.EnrollNumber,-4} {name ?? "(no name)",-28} {a.VerifyName}");
        }
    }

    private static int Sync(DeviceSync sync, AttendanceStore store, string[] args, bool ack, bool trace, int seconds)
    {
        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
        Console.WriteLine($"{Now()} syncing every {seconds}s. Ctrl+C to stop.");
        int failures = 0;
        while (!stop.IsCancellationRequested)
        {
            try
            {
                foreach (var d in SelectedDevices(args, store)) PullOnce(sync, store, d.Id, ack, trace);
                failures = 0;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Console.WriteLine($"{Now()} sync failed ({++failures}): {ex.GetBaseException().Message}");
            }
            stop.Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(seconds));
        }
        return 0;
    }

    // --device <id> picks one device; otherwise every enabled device in the devices table.
    private static List<DeviceConfig> SelectedDevices(string[] args, AttendanceStore store)
    {
        int i = Array.IndexOf(args, "--device");
        if (i >= 0 && i + 1 < args.Length)
        {
            var d = store.GetDevice(args[i + 1]);
            return d is null ? new() : new() { d };
        }
        return store.GetDevices().Where(d => d.Enabled).ToList();
    }

    private static int Devices(string[] args, AttendanceStore store)
    {
        switch (args.Skip(1).FirstOrDefault())
        {
            case "add" when args.Length > 3:
            {
                var id = Guid.NewGuid().ToString("N")[..12];
                var port = args.Length > 4 && int.TryParse(args[4], out var p) ? p : 5005;
                store.SaveDevice(id, args[2], "", args[3], port, enabled: true);
                Console.WriteLine($"Added device {id} ({args[2]}, {args[3]}:{port}).");
                return 0;
            }
            case "remove" when args.Length > 2:
                store.DeleteDevice(args[2]);
                Console.WriteLine("Removed.");
                return 0;
            case "list" or null or "":
                foreach (var d in store.GetDevices())
                    Console.WriteLine($"{d.Id}  {d.Name,-20} {d.Ip}:{d.Port,-6} {(d.Enabled ? "enabled" : "disabled"),-9} serial {(d.Serial.Length > 0 ? d.Serial : "-")}");
                return 0;
            default:
                Console.WriteLine("devices list | devices add <name> <ip> [port] | devices remove <id>");
                return 1;
        }
    }

    private static int Names(string[] args, AttendanceStore store)
    {
        switch (args.Skip(1).FirstOrDefault())
        {
            case "import" when args.Length > 2:
                Console.WriteLine($"Imported {store.ImportNames(args[2])} name(s).");
                return 0;
            case "set" when args.Length > 3 && uint.TryParse(args[2], out var enroll):
                store.SetName(enroll, string.Join(' ', args.Skip(3)));
                Console.WriteLine("Saved.");
                return 0;
            case "list":
                foreach (var (k, v, active) in store.GetPeople().OrderBy(p => p.Enroll)) Console.WriteLine($"{k,4}  {v}{(active ? "" : "  (former)")}");
                return 0;
            case "remove" when args.Length > 2 && uint.TryParse(args[2], out var gone):
                store.DeleteName(gone);
                Console.WriteLine("Removed. Old scans for that ID will show no name.");
                return 0;
            default:
                Console.WriteLine("names import <file.csv> | names set <enroll> <name> | names list");
                return 1;
        }
    }

    private static int IntervalSeconds(string[] args, AppSettings s) => Math.Max(5, ArgInt(args, "--interval", s.SyncIntervalSeconds));

    private static int ArgInt(string[] args, string name, int fallback)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out var v) ? v : fallback;
    }

    private static string Now() => DateTime.Now.ToString("HH:mm:ss");
}
