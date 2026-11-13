using System.Globalization;
using System.Text;
using GozcuStandalone.Device;
using GozcuStandalone.Storage;

namespace GozcuStandalone.Reporting;

public sealed class ReportTable
{
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public List<string> Headers { get; init; } = new();
    public List<string[]> Rows { get; init; } = new();
    public string Footer { get; set; } = "";

    public void WriteCsv(string path)
    {
        static string Esc(string v) => v.Contains(',') || v.Contains('"') || v.Contains('\n') ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(',', Headers.Select(Esc)));
        foreach (var r in Rows) sb.AppendLine(string.Join(',', r.Select(Esc)));
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
    }

    public void WriteXlsx(string path) => XlsxWriter.Write(path, string.IsNullOrWhiteSpace(Title) ? "Report" : Title, Headers, Rows);

    // Chooses the format from the file extension: .xlsx for Excel, anything else is CSV.
    public void Save(string path)
    {
        if (string.Equals(Path.GetExtension(path), ".xlsx", StringComparison.OrdinalIgnoreCase)) WriteXlsx(path);
        else WriteCsv(path);
    }
}

// Everything a report needs, loaded once so a report does not re-read the database for each person.
// With a department chosen, every list and total covers only the people in that department.
public sealed class ReportContext
{
    public required AttendanceStore Store { get; init; }
    public required AppSettings Settings { get; init; }
    public required Dictionary<uint, string> Names { get; init; }
    public required List<(uint Enroll, string Name, bool Active)> People { get; init; }
    public required ShiftBook Shifts { get; init; }
    public required LeaveBook Leaves { get; init; }
    public required Dictionary<DateTime, string> Holidays { get; init; }
    public required List<DepartmentInfo> Departments { get; init; }
    public required Dictionary<uint, int> DepartmentOf { get; init; }
    public int? DepartmentFilter { get; init; }

    public static ReportContext Load(AttendanceStore store, AppSettings settings, int? department = null) => new()
    {
        Store = store,
        Settings = settings,
        Names = store.GetNames(),
        People = store.GetPeople(),
        Shifts = store.LoadShiftBook(),
        Leaves = store.LoadLeaveBook(),
        Holidays = store.GetHolidays(),
        Departments = store.GetDepartments(),
        DepartmentOf = store.GetPersonDepartments(),
        DepartmentFilter = department,
    };

    public string NameOf(uint id) => Names.TryGetValue(id, out var n) ? n : "";

    public bool Allows(uint enroll) =>
        DepartmentFilter is not { } d || (DepartmentOf.TryGetValue(enroll, out var x) && x == d);

    public string DeptName(uint enroll) =>
        DepartmentOf.TryGetValue(enroll, out var id) ? Departments.FirstOrDefault(d => d.Id == id)?.Name ?? "" : "";

    public IEnumerable<(uint Enroll, string Name, bool Active)> ActivePeople => People.Where(p => p.Active && Allows(p.Enroll));

    public List<AttendanceRecord> Scans(DateTime from, DateTime toExclusive) =>
        Store.GetRange(from, toExclusive).Where(r => Allows(r.EnrollNumber)).ToList();

    public List<DailyRow> Daily(IEnumerable<AttendanceRecord> scans) =>
        DailyReport.Build(scans, Names, Settings.WorkStart, Settings.WorkEnd, Shifts);
}

public sealed record AbsenceRow(DateTime Date, uint Enroll, string Name, string Shift);

public static class Reports
{
    private static string Day(DateTime d) => d.ToString("ddd", CultureInfo.InvariantCulture);
    private static string Date(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static string Mins(int m) => m == 0 ? "" : m >= 60 ? Loc.F("{0}h {1:00}m", m / 60, m % 60) : Loc.F("{0} min", m);

    private static string Scope(ReportContext ctx) =>
        ctx.DepartmentFilter is { } d ? " · " + (ctx.Departments.FirstOrDefault(x => x.Id == d)?.Name ?? Loc.T("department")) : "";

    // ---------- who should have been at work but has no scan ----------

    // A person is absent on a day when they are active, have a shift that runs that weekday, are not on
    // leave or a holiday, and have no scan. Today is left out because people may still arrive.
    public static List<AbsenceRow> Absences(ReportContext ctx, DateTime from, DateTime to)
    {
        var last = to.Date >= DateTime.Today ? DateTime.Today.AddDays(-1) : to.Date;
        var result = new List<AbsenceRow>();
        if (last < from.Date) return result;

        var present = ctx.Scans(from.Date, last.AddDays(1))
            .Select(r => (r.Timestamp.Date, r.EnrollNumber)).ToHashSet();

        foreach (var (enroll, name, _) in ctx.ActivePeople)
        {
            for (var d = from.Date; d <= last; d = d.AddDays(1))
            {
                if (!ctx.Shifts.IsWorkday(d, enroll, out var shift)) continue;
                if (ctx.Holidays.ContainsKey(d) || ctx.Leaves.OnLeave(d, enroll) is not null) continue;
                if (present.Contains((d, enroll))) continue;
                result.Add(new AbsenceRow(d, enroll, name, shift!.Name));
            }
        }
        return result.OrderByDescending(a => a.Date).ThenBy(a => a.Name).ToList();
    }

    public static List<(uint Enroll, string Name, string Shift)> NotInToday(ReportContext ctx)
    {
        var today = DateTime.Today;
        if (ctx.Holidays.ContainsKey(today)) return new();
        var present = ctx.Scans(today, today.AddDays(1)).Select(r => r.EnrollNumber).ToHashSet();
        return ctx.ActivePeople
            .Where(p => ctx.Shifts.IsWorkday(today, p.Enroll, out _) && ctx.Leaves.OnLeave(today, p.Enroll) is null && !present.Contains(p.Enroll))
            .Select(p => (p.Enroll, p.Name, ctx.Shifts.For(today, p.Enroll)?.Name ?? ""))
            .OrderBy(p => p.Name)
            .ToList();
    }

    // ---------- tables ----------

    public static ReportTable Monthly(ReportContext ctx, DateTime month)
    {
        var first = new DateTime(month.Year, month.Month, 1);
        var days = ctx.Daily(ctx.Scans(first, first.AddMonths(1)));
        var absent = Absences(ctx, first, first.AddMonths(1).AddDays(-1)).GroupBy(a => a.Enroll).ToDictionary(g => g.Key, g => g.Count());
        var rows = MonthlySummary.Build(days);

        var table = new ReportTable
        {
            Title = "Monthly summary",
            Subtitle = first.ToString("MMMM yyyy", CultureInfo.InvariantCulture) + Scope(ctx),
            Headers = { "ID", "Name", "Department", "Days", "Absent", "Worked", "Avg per day", "Late", "Early leave", "Overtime", "No out scan" },
        };
        foreach (var r in rows)
        {
            table.Rows.Add(new[]
            {
                r.Enroll.ToString(), r.Name.Length > 0 ? r.Name : "—", ctx.DeptName(r.Enroll), r.DaysPresent.ToString(),
                absent.TryGetValue(r.Enroll, out var a) ? a.ToString() : "0",
                MonthlySummary.Hours(r.Worked), MonthlySummary.Hours(r.AveragePerDay),
                r.LateMinutes == 0 ? "—" : Loc.F("{0}d · {1} min", r.LateDays, r.LateMinutes),
                r.EarlyMinutes == 0 ? "—" : Loc.F("{0}d · {1} min", r.EarlyDays, r.EarlyMinutes),
                r.OvertimeMinutes == 0 ? "—" : Mins(r.OvertimeMinutes),
                r.IncompleteDays == 0 ? "—" : r.IncompleteDays.ToString(),
            });
        }
        table.Footer = Loc.F("{0} people. “Absent” counts working days with no scan, leave or holiday. Days with a single scan are left out of the hours.", rows.Count);
        return table;
    }

    public static ReportTable Timesheet(ReportContext ctx, uint enroll, DateTime month)
    {
        var first = new DateTime(month.Year, month.Month, 1);
        var lastDay = first.AddMonths(1).AddDays(-1);
        if (lastDay > DateTime.Today) lastDay = DateTime.Today;

        var scans = ctx.Store.GetRange(first, first.AddMonths(1)).Where(r => r.EnrollNumber == enroll).ToList();
        var daily = ctx.Daily(scans).ToDictionary(d => d.Date);
        var manualDays = scans.Where(s => s.IsManual).Select(s => s.Timestamp.Date).ToHashSet();
        var dept = ctx.DeptName(enroll);

        var table = new ReportTable
        {
            Title = "Timesheet",
            Subtitle = $"{ctx.NameOf(enroll)} (ID {enroll})" + (dept.Length > 0 ? $" · {dept}" : "") + $" · {first.ToString("MMMM yyyy", CultureInfo.InvariantCulture)}",
            Headers = { "Date", "Day", "Shift", "First in", "Last out", "Worked", "Late", "Early leave", "Overtime", "Scans", "Note" },
        };

        TimeSpan worked = TimeSpan.Zero;
        int present = 0, absent = 0, late = 0, early = 0, over = 0;
        for (var d = first; d <= lastDay; d = d.AddDays(1))
        {
            var shift = ctx.Shifts.For(d, enroll);
            if (daily.TryGetValue(d, out var row))
            {
                present++;
                worked += row.Worked; late += row.LateMinutes; early += row.EarlyLeaveMinutes; over += row.OvertimeMinutes;
                var notes = new List<string>();
                if (row.LastOut is null) notes.Add(Loc.T("only one scan"));
                if (manualDays.Contains(d)) notes.Add(Loc.T("manual entry"));
                table.Rows.Add(new[]
                {
                    Date(d), Day(d), row.Shift, row.FirstIn.ToString("HH:mm"), row.LastOut?.ToString("HH:mm") ?? "—",
                    row.Worked == TimeSpan.Zero ? "—" : MonthlySummary.Hours(row.Worked),
                    Mins(row.LateMinutes), Mins(row.EarlyLeaveMinutes), Mins(row.OvertimeMinutes),
                    row.Punches.ToString(), string.Join(", ", notes),
                });
                continue;
            }

            string note;
            if (ctx.Holidays.TryGetValue(d, out var holiday)) note = Loc.F("Holiday: {0}", holiday);
            else if (ctx.Leaves.OnLeave(d, enroll) is { } leave) note = Loc.F("Leave: {0}", leave);
            else if (ctx.Shifts.IsWorkday(d, enroll, out _)) { note = Loc.T("Absent"); absent++; }
            else note = Loc.T("Day off");

            table.Rows.Add(new[] { Date(d), Day(d), shift?.Name ?? "", "", "", "", "", "", "", "0", note });
        }

        table.Footer = Loc.F("Present {0} day(s) · absent {1} · worked {2} · late {3} min · early leave {4} min · overtime {5}", present, absent, MonthlySummary.Hours(worked), late, early, Mins(over));
        return table;
    }

    public static ReportTable Absent(ReportContext ctx, DateTime from, DateTime to)
    {
        var list = Absences(ctx, from, to);
        var table = new ReportTable
        {
            Title = "Absences",
            Subtitle = Loc.F("{0} to {1}", Date(from), Date(to)) + Scope(ctx),
            Headers = { "Date", "Day", "ID", "Name", "Department", "Shift" },
        };
        foreach (var a in list) table.Rows.Add(new[] { Date(a.Date), Day(a.Date), a.Enroll.ToString(), a.Name, ctx.DeptName(a.Enroll), a.Shift });
        table.Footer = Loc.F("{0} absence(s) across {1} people. Leave and holidays are not counted.", list.Count, list.Select(a => a.Enroll).Distinct().Count());
        return table;
    }

    public static ReportTable Late(ReportContext ctx, DateTime from, DateTime to)
    {
        var rows = ctx.Daily(ctx.Scans(from.Date, to.Date.AddDays(1))).Where(r => r.LateMinutes > 0)
            .OrderByDescending(r => r.Date).ThenByDescending(r => r.LateMinutes).ToList();
        var table = new ReportTable
        {
            Title = "Late arrivals",
            Subtitle = Loc.F("{0} to {1}", Date(from), Date(to)) + Scope(ctx),
            Headers = { "Date", "Day", "ID", "Name", "Department", "Shift", "First in", "Late" },
        };
        foreach (var r in rows)
            table.Rows.Add(new[] { Date(r.Date), Day(r.Date), r.Enroll.ToString(), r.Name, ctx.DeptName(r.Enroll), r.Shift, r.FirstIn.ToString("HH:mm"), Mins(r.LateMinutes) });
        table.Footer = Loc.F("{0} late arrival(s), {1} in total. Arrivals inside the shift's grace time are not counted.", rows.Count, Mins(rows.Sum(r => r.LateMinutes)));
        return table;
    }

    public static ReportTable EarlyLeave(ReportContext ctx, DateTime from, DateTime to)
    {
        var rows = ctx.Daily(ctx.Scans(from.Date, to.Date.AddDays(1))).Where(r => r.EarlyLeaveMinutes > 0)
            .OrderByDescending(r => r.Date).ThenByDescending(r => r.EarlyLeaveMinutes).ToList();
        var table = new ReportTable
        {
            Title = "Early leave",
            Subtitle = Loc.F("{0} to {1}", Date(from), Date(to)) + Scope(ctx),
            Headers = { "Date", "Day", "ID", "Name", "Department", "Shift", "Last out", "Early by" },
        };
        foreach (var r in rows)
            table.Rows.Add(new[] { Date(r.Date), Day(r.Date), r.Enroll.ToString(), r.Name, ctx.DeptName(r.Enroll), r.Shift, r.LastOut?.ToString("HH:mm") ?? "", Mins(r.EarlyLeaveMinutes) });
        table.Footer = Loc.F("{0} early departure(s), {1} in total.", rows.Count, Mins(rows.Sum(r => r.EarlyLeaveMinutes)));
        return table;
    }

    public static ReportTable Overtime(ReportContext ctx, DateTime from, DateTime to)
    {
        var rows = ctx.Daily(ctx.Scans(from.Date, to.Date.AddDays(1))).Where(r => r.OvertimeMinutes > 0)
            .OrderByDescending(r => r.Date).ThenByDescending(r => r.OvertimeMinutes).ToList();
        var table = new ReportTable
        {
            Title = "Overtime",
            Subtitle = Loc.F("{0} to {1}", Date(from), Date(to)) + Scope(ctx),
            Headers = { "Date", "Day", "ID", "Name", "Department", "Shift", "Last out", "Overtime" },
        };
        foreach (var r in rows)
            table.Rows.Add(new[] { Date(r.Date), Day(r.Date), r.Enroll.ToString(), r.Name, ctx.DeptName(r.Enroll), r.Shift, r.LastOut?.ToString("HH:mm") ?? "", Mins(r.OvertimeMinutes) });
        table.Footer = Loc.F("{0} day(s) with overtime, {1} in total. Leaving inside the shift's grace time is not overtime.", rows.Count, Mins(rows.Sum(r => r.OvertimeMinutes)));
        return table;
    }

    // ---------- today, for the dashboard cards ----------

    public static ReportTable Today(ReportContext ctx, string kind)
    {
        var today = DateTime.Today;
        var scans = ctx.Scans(today, today.AddDays(1));
        var daily = ctx.Daily(scans);
        var subtitle = today.ToString("dddd, d MMMM yyyy", CultureInfo.InvariantCulture) + Scope(ctx);

        switch (kind)
        {
            case "present":
            {
                var table = new ReportTable { Title = "Present today", Subtitle = subtitle, Headers = { "ID", "Name", "Department", "Shift", "First in", "Last out", "Scans", "Late" } };
                foreach (var r in daily.OrderBy(r => r.FirstIn))
                    table.Rows.Add(new[] { r.Enroll.ToString(), r.Name, ctx.DeptName(r.Enroll), r.Shift, r.FirstIn.ToString("HH:mm"), r.LastOut?.ToString("HH:mm") ?? "—", r.Punches.ToString(), Mins(r.LateMinutes) });
                table.Footer = Loc.F("{0} people have scanned in today. Double-click a person for their monthly timesheet.", daily.Count);
                return table;
            }

            case "late":
            {
                var late = daily.Where(r => r.LateMinutes > 0).OrderByDescending(r => r.LateMinutes).ToList();
                var table = new ReportTable { Title = "Late today", Subtitle = subtitle, Headers = { "ID", "Name", "Department", "Shift", "First in", "Late by" } };
                foreach (var r in late)
                    table.Rows.Add(new[] { r.Enroll.ToString(), r.Name, ctx.DeptName(r.Enroll), r.Shift, r.FirstIn.ToString("HH:mm"), Mins(r.LateMinutes) });
                table.Footer = Loc.F("{0} late arrival(s). Arrivals inside the shift's grace time are not counted. Double-click a person for their timesheet.", late.Count);
                return table;
            }

            case "early":
            {
                var early = daily.Where(r => r.EarlyLeaveMinutes > 0).OrderByDescending(r => r.EarlyLeaveMinutes).ToList();
                var table = new ReportTable { Title = "Left early today", Subtitle = subtitle, Headers = { "ID", "Name", "Department", "Shift", "Last out", "Early by" } };
                foreach (var r in early)
                    table.Rows.Add(new[] { r.Enroll.ToString(), r.Name, ctx.DeptName(r.Enroll), r.Shift, r.LastOut?.ToString("HH:mm") ?? "", Mins(r.EarlyLeaveMinutes) });
                table.Footer = Loc.F("{0} early departure(s). Only people who have scanned out are counted. Double-click a person for their timesheet.", early.Count);
                return table;
            }

            case "notin":
            {
                var present = scans.Select(r => r.EnrollNumber).ToHashSet();
                var table = new ReportTable { Title = "Expected today, not in yet", Subtitle = subtitle, Headers = { "ID", "Name", "Department", "Shift", "Status" } };
                int notIn = 0, away = 0;
                foreach (var p in ctx.ActivePeople.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
                {
                    if (present.Contains(p.Enroll) || !ctx.Shifts.IsWorkday(today, p.Enroll, out var shift)) continue;
                    string status;
                    if (ctx.Holidays.TryGetValue(today, out var holiday)) { status = Loc.F("Holiday: {0}", holiday); away++; }
                    else if (ctx.Leaves.OnLeave(today, p.Enroll) is { } leave) { status = Loc.F("On leave: {0}", leave); away++; }
                    else { status = Loc.T("Not in yet"); notIn++; }
                    table.Rows.Add(new[] { p.Enroll.ToString(), p.Name, ctx.DeptName(p.Enroll), shift!.Name, status });
                }
                table.Rows.Sort((a, b) => string.Compare(a[4] == Loc.T("Not in yet") ? "0" : "1", b[4] == Loc.T("Not in yet") ? "0" : "1", StringComparison.Ordinal));
                table.Footer = Loc.F("{0} not in yet, {1} on leave or holiday. Double-click a person for their timesheet.", notIn, away);
                return table;
            }

            default:
            {
                var table = new ReportTable { Title = "Scans today", Subtitle = subtitle, Headers = { "Time", "ID", "Name", "Method", "Note" } };
                foreach (var s in scans.OrderByDescending(s => s.Timestamp))
                    table.Rows.Add(new[] { s.Timestamp.ToString("HH:mm:ss", CultureInfo.InvariantCulture), s.EnrollNumber.ToString(), ctx.NameOf(s.EnrollNumber), s.VerifyName, s.Note ?? "" });
                table.Footer = Loc.F("{0} scan(s) today.", scans.Count);
                return table;
            }
        }
    }

    // Every manual entry and every voided scan, for review.
    public static ReportTable Corrections(ReportContext ctx, DateTime from, DateTime to)
    {
        var list = ctx.Store.GetCorrections(from.Date, to.Date.AddDays(1)).Where(c => ctx.Allows(c.Record.EnrollNumber)).ToList();
        var table = new ReportTable
        {
            Title = "Manual entries and voided scans",
            Subtitle = Loc.F("{0} to {1}", Date(from), Date(to)) + Scope(ctx),
            Headers = { "When", "ID", "Name", "Kind", "Status", "Note", "Changed by", "Recorded at" },
        };
        foreach (var (r, by, at) in list)
        {
            table.Rows.Add(new[]
            {
                r.Timestamp.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), r.EnrollNumber.ToString(), ctx.NameOf(r.EnrollNumber),
                r.IsManual ? Loc.T("Manual entry") : r.VerifyName, r.Voided ? Loc.T("Voided") : Loc.T("Counted"), r.Note ?? "", by ?? "", at ?? "",
            });
        }
        table.Footer = Loc.F(list.Count == 1 ? "{0} entry." : "{0} entries.", list.Count);
        return table;
    }
}
