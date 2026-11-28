using System.Globalization;
using System.Text;
using GozcuStandalone.Device;
using GozcuStandalone.Storage;

namespace GozcuStandalone.Reporting;

// How a row should stand out: a missing day is highlighted red so it is not mistaken for a normal one.
public enum ReportRowKind { Normal, Missing }

public sealed class ReportTable
{
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public List<string> Headers { get; init; } = new();
    public List<string[]> Rows { get; init; } = new();
    // Parallel to Rows; defaults to Normal so a row added without a kind is never flagged.
    public List<ReportRowKind> RowKinds { get; init; } = new();
    // Parallel to Rows: "enroll\tdate" for a missing day that can be marked full, null otherwise.
    public List<string?> MarkFullKeys { get; init; } = new();
    // Parallel to Rows: "enroll\tdate" for a day whose scans can be opened for editing, null otherwise.
    public List<string?> EditKeys { get; init; } = new();
    public string Footer { get; set; } = "";

    public void AddRow(string[] cells, ReportRowKind kind = ReportRowKind.Normal, (uint Enroll, DateTime Date)? markFull = null, (uint Enroll, DateTime Date)? editDay = null)
    {
        Rows.Add(cells);
        RowKinds.Add(kind);
        MarkFullKeys.Add(markFull is { } m ? $"{m.Enroll}\t{m.Date:yyyy-MM-dd}" : null);
        EditKeys.Add(editDay is { } e ? $"{e.Enroll}\t{e.Date:yyyy-MM-dd}" : null);
    }

    public ReportRowKind KindOf(int rowIndex) => rowIndex >= 0 && rowIndex < RowKinds.Count ? RowKinds[rowIndex] : ReportRowKind.Normal;

    public string? MarkFullKeyOf(int rowIndex) => rowIndex >= 0 && rowIndex < MarkFullKeys.Count ? MarkFullKeys[rowIndex] : null;

    public string? EditKeyOf(int rowIndex) => rowIndex >= 0 && rowIndex < EditKeys.Count ? EditKeys[rowIndex] : null;

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
    public required AlwaysPresentBook AlwaysPresent { get; init; }
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
        AlwaysPresent = store.LoadAlwaysPresentBook(),
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

public sealed record AbsenceRow(DateTime Date, uint Enroll, string Name, string Shift, string Status = "Absent");

// Quick facts about one person for one month: how reliably they show up, and how.
public sealed record PersonProfile(
    uint Enroll, string Name, string Department, DateTime Month,
    int DaysPresent, int Absences, int LateDays, int LateMinutesTotal, int EarlyDays, int EarlyMinutesTotal, int OvertimeMinutes, TimeSpan Worked)
{
    public int AverageLateMinutes => LateDays > 0 ? (int)Math.Round((double)LateMinutesTotal / LateDays) : 0;
    public int AverageEarlyMinutes => EarlyDays > 0 ? (int)Math.Round((double)EarlyMinutesTotal / EarlyDays) : 0;
}

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
            if (ctx.AlwaysPresent.IsAlwaysPresent(enroll)) continue;
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

    // Same no-scan days as Absences, but also includes holidays and approved leave with their own status
    // label instead of skipping them, so the Attendance page can show why someone has no scan that day.
    public static List<AbsenceRow> NonPresentDays(ReportContext ctx, DateTime from, DateTime to)
    {
        var last = to.Date >= DateTime.Today ? DateTime.Today.AddDays(-1) : to.Date;
        var result = new List<AbsenceRow>();
        if (last < from.Date) return result;

        var present = ctx.Scans(from.Date, last.AddDays(1))
            .Select(r => (r.Timestamp.Date, r.EnrollNumber)).ToHashSet();

        foreach (var (enroll, name, _) in ctx.ActivePeople)
        {
            if (ctx.AlwaysPresent.IsAlwaysPresent(enroll)) continue;
            for (var d = from.Date; d <= last; d = d.AddDays(1))
            {
                if (!ctx.Shifts.IsWorkday(d, enroll, out var shift)) continue;
                if (present.Contains((d, enroll))) continue;

                string status = ctx.Holidays.TryGetValue(d, out var holiday) ? Loc.F("Holiday: {0}", holiday)
                    : ctx.Leaves.OnLeave(d, enroll) is { } leave ? Loc.F("Leave: {0}", leave)
                    : Loc.T("Absent");
                result.Add(new AbsenceRow(d, enroll, name, shift!.Name, status));
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
            .Where(p => !ctx.AlwaysPresent.IsAlwaysPresent(p.Enroll) &&
                        ctx.Shifts.IsWorkday(today, p.Enroll, out _) && ctx.Leaves.OnLeave(today, p.Enroll) is null && !present.Contains(p.Enroll))
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
                table.AddRow(new[]
                {
                    Date(d), Day(d), row.Shift, row.FirstIn.ToString("HH:mm"), row.LastOut?.ToString("HH:mm") ?? "—",
                    row.Worked == TimeSpan.Zero ? "—" : MonthlySummary.Hours(row.Worked),
                    Mins(row.LateMinutes), Mins(row.EarlyLeaveMinutes), Mins(row.OvertimeMinutes),
                    row.Punches.ToString(), string.Join(", ", notes),
                }, editDay: (enroll, d));
                continue;
            }

            string note;
            var missing = false;
            if (ctx.Holidays.TryGetValue(d, out var holiday)) note = Loc.F("Holiday: {0}", holiday);
            else if (ctx.Leaves.OnLeave(d, enroll) is { } leave) note = Loc.F("Leave: {0}", leave);
            else if (ctx.Shifts.IsWorkday(d, enroll, out _)) { note = Loc.T("Absent"); absent++; missing = d < DateTime.Today; }
            else note = Loc.T("Day off");

            table.AddRow(new[] { Date(d), Day(d), shift?.Name ?? "", "", "", "", "", "", "", "0", note },
                missing ? ReportRowKind.Missing : ReportRowKind.Normal, missing ? (enroll, d) : null);
        }

        table.Footer = Loc.F("Present {0} day(s) · absent {1} · worked {2} · late {3} min · early leave {4} min · overtime {5}", present, absent, MonthlySummary.Hours(worked), late, early, Mins(over));
        return table;
    }

    // Quick facts about one person for a month, for the profile card shown from People, Attendance and Reports.
    public static PersonProfile Profile(ReportContext ctx, uint enroll, DateTime month)
    {
        var first = new DateTime(month.Year, month.Month, 1);
        var lastDay = first.AddMonths(1).AddDays(-1);
        var today = DateTime.Today;
        var toDate = lastDay > today ? today : lastDay;

        var name = ctx.NameOf(enroll);
        var dept = ctx.DeptName(enroll);
        var scans = ctx.Store.GetRange(first, first.AddMonths(1)).Where(r => r.EnrollNumber == enroll).ToList();
        var monthRow = MonthlySummary.Build(ctx.Daily(scans)).FirstOrDefault(r => r.Enroll == enroll);
        var absencesThisMonth = toDate >= first ? Absences(ctx, first, toDate).Count(a => a.Enroll == enroll) : 0;

        return new PersonProfile(
            enroll, name, dept, first,
            DaysPresent: monthRow?.DaysPresent ?? 0,
            Absences: absencesThisMonth,
            LateDays: monthRow?.LateDays ?? 0,
            LateMinutesTotal: monthRow?.LateMinutes ?? 0,
            EarlyDays: monthRow?.EarlyDays ?? 0,
            EarlyMinutesTotal: monthRow?.EarlyMinutes ?? 0,
            OvertimeMinutes: monthRow?.OvertimeMinutes ?? 0,
            Worked: monthRow?.Worked ?? TimeSpan.Zero);
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
        foreach (var a in list)
        {
            var canMark = a.Date < DateTime.Today;
            table.AddRow(new[] { Date(a.Date), Day(a.Date), a.Enroll.ToString(), a.Name, ctx.DeptName(a.Enroll), a.Shift },
                ReportRowKind.Missing, canMark ? (a.Enroll, a.Date) : null);
        }
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

    // A row standing in for a real scan, for a person an admin has marked as always present.
    // Used only for a day they have no real scan, so a real scan always wins.
    private static DailyRow AlwaysPresentRow(ReportContext ctx, DateTime day, uint enroll, AlwaysPresentEntry entry)
    {
        var shift = ctx.Shifts.For(day, enroll);
        var first = day.Date + entry.FirstIn;
        var last = day.Date + entry.LastOut;
        return new DailyRow(day.Date, enroll, ctx.NameOf(enroll), shift?.Name ?? "", first, last, 0, last - first, 0, 0, 0);
    }

    // The daily rows for today, plus a synthetic row for every active always-present person
    // who has no real scan yet, so they show as present without ever badging in.
    private static List<DailyRow> WithAlwaysPresent(ReportContext ctx, List<DailyRow> daily, DateTime today)
    {
        var present = daily.Select(r => r.Enroll).ToHashSet();
        foreach (var (enroll, _, _) in ctx.ActivePeople)
        {
            if (present.Contains(enroll)) continue;
            var entry = ctx.AlwaysPresent.For(enroll);
            if (entry is null) continue;
            daily.Add(AlwaysPresentRow(ctx, today, enroll, entry));
        }
        return daily;
    }

    public static ReportTable Today(ReportContext ctx, string kind)
    {
        var today = DateTime.Today;
        var scans = ctx.Scans(today, today.AddDays(1));
        var daily = WithAlwaysPresent(ctx, ctx.Daily(scans), today);
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
                var present = daily.Select(r => r.Enroll).ToHashSet();
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
