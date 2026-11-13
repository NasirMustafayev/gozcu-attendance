namespace GozcuStandalone;

public sealed record ShiftDef(
    int Id,
    string Name,
    TimeSpan Start,
    TimeSpan End,
    int EarlyEnterMinutes,
    int LateEnterMinutes,
    int EarlyLeaveMinutes,
    int LateLeaveMinutes,
    int DaysOfWeekMask,
    int BreakMinutes = 0)
{
    // Bit 0 is Monday ... bit 6 is Sunday; the original software's Standart shift uses 63 (Monday to Saturday).
    public bool RunsOn(DateTime day) => (DaysOfWeekMask & (1 << ((int)day.DayOfWeek + 6) % 7)) != 0;

    public string Code => Name.Length > 0 ? Name[..1].ToUpperInvariant() : "?";

    public static readonly string[] DayNames = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };

    public string DaysText
    {
        get
        {
            var days = Enumerable.Range(0, 7).Where(i => (DaysOfWeekMask & (1 << i)) != 0).Select(i => DayNames[i]).ToList();
            return days.Count == 7 ? Loc.T("Every day") : days.Count == 0 ? "—" : string.Join(" ", days.Select(Loc.T));
        }
    }
}

// Which shift each person works on each date.
public sealed class ShiftBook
{
    private readonly Dictionary<int, ShiftDef> _shifts;
    private readonly Dictionary<(DateTime Date, uint Enroll), int> _assignments;

    public ShiftBook(IEnumerable<ShiftDef> shifts, Dictionary<(DateTime, uint), int> assignments)
    {
        _shifts = shifts.ToDictionary(s => s.Id);
        _assignments = assignments;
    }

    public int ShiftCount => _shifts.Count;
    public int AssignmentCount => _assignments.Count;
    public IEnumerable<ShiftDef> Shifts => _shifts.Values;

    public ShiftDef? For(DateTime date, uint enroll) =>
        _assignments.TryGetValue((date.Date, enroll), out var id) && _shifts.TryGetValue(id, out var s) ? s : null;

    // A working day is a day the person has a shift AND that shift runs on that weekday.
    public bool IsWorkday(DateTime date, uint enroll, out ShiftDef? shift)
    {
        shift = For(date, enroll);
        return shift is not null && shift.RunsOn(date);
    }
}

public sealed record LeaveEntry(uint Enroll, DateTime Start, DateTime End, string Description, int Type);

public sealed class LeaveBook
{
    private readonly Dictionary<uint, List<LeaveEntry>> _byPerson;

    public LeaveBook(IEnumerable<LeaveEntry> leaves) =>
        _byPerson = leaves.GroupBy(l => l.Enroll).ToDictionary(g => g.Key, g => g.ToList());

    public int Count => _byPerson.Values.Sum(l => l.Count);

    public string? OnLeave(DateTime date, uint enroll) =>
        _byPerson.TryGetValue(enroll, out var list)
            ? list.FirstOrDefault(l => date.Date >= l.Start.Date && date.Date <= l.End.Date)?.Description
            : null;
}

public static class LeaveTypes
{
    // Types 1..9 come from the original software; the rest are created in this app.
    public static readonly (int Type, string Label)[] Local =
    {
        (10, "Annual leave"), (11, "Sick leave"), (12, "Unpaid leave"), (13, "Business trip"), (14, "Other"),
    };
}
