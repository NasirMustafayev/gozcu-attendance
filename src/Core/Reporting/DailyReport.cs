using System.Text;
using GozcuStandalone.Device;

namespace GozcuStandalone.Reporting;

public sealed record DailyRow(
    DateTime Date,
    uint Enroll,
    string Name,
    string Shift,
    DateTime FirstIn,
    DateTime? LastOut,
    int Punches,
    TimeSpan Worked,
    int LateMinutes,
    int EarlyLeaveMinutes,
    int OvertimeMinutes);

public static class DailyReport
{
    // The device does not report in/out, so the first scan of the day is "in" and the last is "out".
    // A lone scan (or two within five minutes) counts as presence only, with no worked time.
    //
    // With a shift book, each person's own shift decides the rules:
    //   late      = arrival after shift start + late-enter tolerance, counted from shift start
    //   early out = leaving before shift end - early-leave tolerance
    //   overtime  = leaving after shift end + late-leave tolerance, counted from shift end
    // Without one, the global work start and end are used with no tolerances.
    public static List<DailyRow> Build(
        IEnumerable<AttendanceRecord> records,
        IReadOnlyDictionary<uint, string> names,
        TimeSpan workStart,
        TimeSpan workEnd,
        ShiftBook? shifts = null)
    {
        var rows = new List<DailyRow>();
        foreach (var g in records.GroupBy(r => (r.Timestamp.Date, r.EnrollNumber)))
        {
            var scans = g.OrderBy(r => r.Timestamp).ToList();
            var first = scans[0].Timestamp;
            var last = scans[^1].Timestamp;
            DateTime? lastOut = (last - first).TotalMinutes >= 5 ? last : null;

            var shift = shifts?.For(g.Key.Date, g.Key.EnrollNumber);
            var start = shift?.Start ?? workStart;
            var end = shift?.End ?? workEnd;
            var lateTol = TimeSpan.FromMinutes(shift?.LateEnterMinutes ?? 0);
            var earlyTol = TimeSpan.FromMinutes(shift?.EarlyLeaveMinutes ?? 0);
            var overTol = TimeSpan.FromMinutes(shift?.LateLeaveMinutes ?? 0);

            // An unpaid break is taken off only for a real working stretch, so a short visit loses nothing.
            var span = last - first;
            var worked = lastOut is null ? TimeSpan.Zero : span;
            if (lastOut is not null && shift is { BreakMinutes: > 0 } && span >= TimeSpan.FromHours(5))
                worked = span - TimeSpan.FromMinutes(shift.BreakMinutes);

            int late = first.TimeOfDay > start + lateTol ? (int)Math.Round((first.TimeOfDay - start).TotalMinutes) : 0;
            int early = lastOut is { } o1 && o1.TimeOfDay < end - earlyTol ? (int)Math.Round((end - o1.TimeOfDay).TotalMinutes) : 0;
            int overtime = lastOut is { } o2 && o2.TimeOfDay > end + overTol ? (int)Math.Round((o2.TimeOfDay - end).TotalMinutes) : 0;

            names.TryGetValue(g.Key.EnrollNumber, out var name);
            rows.Add(new DailyRow(
                g.Key.Date, g.Key.EnrollNumber, name ?? "", shift?.Name ?? "", first, lastOut, scans.Count,
                worked, late, early, overtime));
        }
        return rows.OrderByDescending(r => r.Date).ThenBy(r => r.Name).ThenBy(r => r.Enroll).ToList();
    }

    public static void WriteCsv(string path, IEnumerable<DailyRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Date,EnrollNumber,Name,Shift,FirstIn,LastOut,Punches,Worked,LateMinutes,EarlyLeaveMinutes,OvertimeMinutes");
        foreach (var r in rows)
        {
            sb.AppendLine(string.Join(',',
                r.Date.ToString("yyyy-MM-dd"),
                r.Enroll,
                "\"" + r.Name.Replace("\"", "\"\"") + "\"",
                "\"" + r.Shift.Replace("\"", "\"\"") + "\"",
                r.FirstIn.ToString("HH:mm"),
                r.LastOut?.ToString("HH:mm") ?? "",
                r.Punches,
                r.Worked == TimeSpan.Zero ? "" : $"{(int)r.Worked.TotalHours:00}:{r.Worked.Minutes:00}",
                r.LateMinutes,
                r.EarlyLeaveMinutes,
                r.OvertimeMinutes));
        }
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
    }
}
