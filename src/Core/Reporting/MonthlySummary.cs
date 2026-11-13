using System.Text;

namespace GozcuStandalone.Reporting;

public sealed record MonthRow(
    uint Enroll,
    string Name,
    int DaysPresent,
    int IncompleteDays,
    TimeSpan Worked,
    int LateDays,
    int LateMinutes,
    int EarlyDays,
    int EarlyMinutes,
    int OvertimeMinutes)
{
    // Days with both an arrival and a departure scan; only these count toward hours.
    public int CompleteDays => DaysPresent - IncompleteDays;

    public TimeSpan AveragePerDay => CompleteDays > 0 ? Worked / CompleteDays : TimeSpan.Zero;
}

public static class MonthlySummary
{
    public static List<MonthRow> Build(IEnumerable<DailyRow> days)
    {
        return days
            .GroupBy(d => d.Enroll)
            .Select(g =>
            {
                var list = g.ToList();
                return new MonthRow(
                    g.Key,
                    list.Select(d => d.Name).FirstOrDefault(n => n.Length > 0) ?? "",
                    list.Count,
                    list.Count(d => d.LastOut is null),
                    TimeSpan.FromTicks(list.Sum(d => d.Worked.Ticks)),
                    list.Count(d => d.LateMinutes > 0),
                    list.Sum(d => d.LateMinutes),
                    list.Count(d => d.EarlyLeaveMinutes > 0),
                    list.Sum(d => d.EarlyLeaveMinutes),
                    list.Sum(d => d.OvertimeMinutes));
            })
            .OrderBy(r => r.Name.Length == 0)
            .ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(r => r.Enroll)
            .ToList();
    }

    public static string Hours(TimeSpan t) => t == TimeSpan.Zero ? "—" : Loc.F("{0}h {1:00}m", (int)t.TotalHours, t.Minutes);

    public static void WriteCsv(string path, IEnumerable<MonthRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("EnrollNumber,Name,DaysPresent,IncompleteDays,WorkedHours,AvgHoursPerDay,LateDays,LateMinutes,EarlyLeaveDays,EarlyLeaveMinutes,OvertimeMinutes");
        foreach (var r in rows)
        {
            sb.AppendLine(string.Join(',',
                r.Enroll,
                "\"" + r.Name.Replace("\"", "\"\"") + "\"",
                r.DaysPresent,
                r.IncompleteDays,
                r.Worked.TotalHours.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                r.AveragePerDay.TotalHours.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                r.LateDays,
                r.LateMinutes,
                r.EarlyDays,
                r.EarlyMinutes,
                r.OvertimeMinutes));
        }
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
    }
}
