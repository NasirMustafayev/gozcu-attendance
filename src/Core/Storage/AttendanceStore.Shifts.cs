using System.Globalization;
using Microsoft.Data.Sqlite;

namespace GozcuStandalone.Storage;

public sealed partial class AttendanceStore
{
    // Shifts copied from the original software keep their ids (below 1000);
    // shifts created in this app start at 1000 so the two never collide.
    private const int LocalShiftIdStart = 1000;

    public List<ShiftDef> GetShifts()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT shift_id, name, start_time, end_time, early_enter_min, late_enter_min, early_leave_min, late_leave_min, days_mask, break_min FROM shifts ORDER BY shift_id;";
        var shifts = new List<ShiftDef>();
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            shifts.Add(new ShiftDef(
                rd.GetInt32(0), rd.GetString(1),
                TimeSpan.Parse(rd.GetString(2), CultureInfo.InvariantCulture), TimeSpan.Parse(rd.GetString(3), CultureInfo.InvariantCulture),
                rd.GetInt32(4), rd.GetInt32(5), rd.GetInt32(6), rd.GetInt32(7), rd.GetInt32(8), rd.GetInt32(9)));
        }
        return shifts;
    }

    // Id 0 creates a new shift; any other id updates that shift. Returns the shift id.
    public int UpsertShift(ShiftDef shift)
    {
        using var conn = Open();
        int id = shift.Id;
        if (id == 0)
        {
            using var next = conn.CreateCommand();
            next.CommandText = "SELECT COALESCE(MAX(shift_id), 0) FROM shifts;";
            id = Math.Max(LocalShiftIdStart, Convert.ToInt32(next.ExecuteScalar()) + 1);
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO shifts (shift_id, name, start_time, end_time, early_enter_min, late_enter_min, early_leave_min, late_leave_min, days_mask, break_min)
            VALUES ($id, $n, $s, $e, $a, $b, $c, $d, $m, $k)
            ON CONFLICT(shift_id) DO UPDATE SET
                name = excluded.name, start_time = excluded.start_time, end_time = excluded.end_time,
                early_enter_min = excluded.early_enter_min, late_enter_min = excluded.late_enter_min,
                early_leave_min = excluded.early_leave_min, late_leave_min = excluded.late_leave_min,
                days_mask = excluded.days_mask, break_min = excluded.break_min;
            """;
        BindShift(cmd, id, shift);
        cmd.ExecuteNonQuery();
        return id;
    }

    private static void BindShift(SqliteCommand cmd, int id, ShiftDef shift)
    {
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$n", shift.Name);
        cmd.Parameters.AddWithValue("$s", shift.Start.ToString(@"hh\:mm", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$e", shift.End.ToString(@"hh\:mm", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$a", shift.EarlyEnterMinutes);
        cmd.Parameters.AddWithValue("$b", shift.LateEnterMinutes);
        cmd.Parameters.AddWithValue("$c", shift.EarlyLeaveMinutes);
        cmd.Parameters.AddWithValue("$d", shift.LateLeaveMinutes);
        cmd.Parameters.AddWithValue("$m", shift.DaysOfWeekMask);
        cmd.Parameters.AddWithValue("$k", shift.BreakMinutes);
    }

    public void DeleteShift(int shiftId)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "DELETE FROM shift_assignments WHERE shift_id = $id; DELETE FROM shifts WHERE shift_id = $id;";
        cmd.Parameters.AddWithValue("$id", shiftId);
        cmd.ExecuteNonQuery();
        tx.Commit();
    }

    public int CountAssignments(int shiftId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM shift_assignments WHERE shift_id = $id;";
        cmd.Parameters.AddWithValue("$id", shiftId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    // Importing never overwrites anything changed here: a shift that already exists keeps its edited rules,
    // and an assignment that already exists keeps its shift. Only shifts and days that are new are added.
    public void SaveShifts(IEnumerable<ShiftDef> shifts, IEnumerable<(DateTime Date, uint Enroll, int ShiftId)> assignments)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();

        foreach (var shift in shifts)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT OR IGNORE INTO shifts (shift_id, name, start_time, end_time, early_enter_min, late_enter_min, early_leave_min, late_leave_min, days_mask, break_min)
                VALUES ($id, $n, $s, $e, $a, $b, $c, $d, $m, $k);
                """;
            BindShift(cmd, shift.Id, shift);
            cmd.ExecuteNonQuery();
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT OR IGNORE INTO shift_assignments (work_date, enroll_number, shift_id) VALUES ($d, $e, $s);";
            var d = cmd.Parameters.Add("$d", SqliteType.Text);
            var e = cmd.Parameters.Add("$e", SqliteType.Integer);
            var s2 = cmd.Parameters.Add("$s", SqliteType.Integer);
            foreach (var (date, enroll, shiftId) in assignments)
            {
                d.Value = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                e.Value = (long)enroll;
                s2.Value = shiftId;
                cmd.ExecuteNonQuery();
            }
        }
        tx.Commit();
    }

    // Puts the given people on a shift for every day in the range (or clears them when shiftId is null).
    // With onlyShiftDays, days the shift does not run on (for example Sundays) are skipped.
    public int AssignShift(IEnumerable<uint> people, DateTime from, DateTime to, int? shiftId, bool onlyShiftDays = true)
    {
        var shift = shiftId is { } id ? GetShifts().FirstOrDefault(s => s.Id == id) : null;
        if (shiftId is not null && shift is null) throw new ArgumentException("Unknown shift.");

        int written = 0;
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = shiftId is null
            ? "DELETE FROM shift_assignments WHERE work_date = $d AND enroll_number = $e;"
            : "INSERT OR REPLACE INTO shift_assignments (work_date, enroll_number, shift_id) VALUES ($d, $e, $s);";
        var pd = cmd.Parameters.Add("$d", SqliteType.Text);
        var pe = cmd.Parameters.Add("$e", SqliteType.Integer);
        var ps = shiftId is null ? null : cmd.Parameters.Add("$s", SqliteType.Integer);

        foreach (var person in people)
        {
            for (var day = from.Date; day <= to.Date; day = day.AddDays(1))
            {
                if (shift is not null && onlyShiftDays && !shift.RunsOn(day)) continue;
                pd.Value = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                pe.Value = (long)person;
                if (ps is not null) ps.Value = shiftId;
                written += cmd.ExecuteNonQuery();
            }
        }
        tx.Commit();
        return written;
    }

    public ShiftBook LoadShiftBook()
    {
        var shifts = GetShifts();
        var assignments = new Dictionary<(DateTime, uint), int>();
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT work_date, enroll_number, shift_id FROM shift_assignments;";
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
            assignments[(DateTime.ParseExact(rd.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture), (uint)rd.GetInt64(1))] = rd.GetInt32(2);
        return new ShiftBook(shifts, assignments);
    }

    // ---------- leaves and holidays ----------

    // Replaces only the leave copied from the original software; leave entered here is kept.
    public void SaveLeaves(IEnumerable<LeaveEntry> leaves)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        using (var clear = conn.CreateCommand())
        {
            clear.Transaction = tx;
            clear.CommandText = "DELETE FROM leaves WHERE source = 'pdks';";
            clear.ExecuteNonQuery();
        }
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO leaves (enroll_number, start_date, end_date, description, leave_type, source) VALUES ($e, $s, $d, $n, $t, 'pdks');";
        var e = cmd.Parameters.Add("$e", SqliteType.Integer);
        var s = cmd.Parameters.Add("$s", SqliteType.Text);
        var d = cmd.Parameters.Add("$d", SqliteType.Text);
        var n = cmd.Parameters.Add("$n", SqliteType.Text);
        var t = cmd.Parameters.Add("$t", SqliteType.Integer);
        foreach (var l in leaves)
        {
            e.Value = (long)l.Enroll;
            s.Value = l.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            d.Value = l.End.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            n.Value = l.Description;
            t.Value = l.Type;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public LeaveBook LoadLeaveBook()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT enroll_number, start_date, end_date, description, leave_type FROM leaves;";
        var list = new List<LeaveEntry>();
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            list.Add(new LeaveEntry(
                (uint)rd.GetInt64(0),
                DateTime.ParseExact(rd.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                DateTime.ParseExact(rd.GetString(2), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                rd.GetString(3), rd.GetInt32(4)));
        }
        return new LeaveBook(list);
    }

    public Dictionary<DateTime, string> GetHolidays()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT date, name FROM holidays ORDER BY date;";
        var map = new Dictionary<DateTime, string>();
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
            map[DateTime.ParseExact(rd.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture)] = rd.GetString(1);
        return map;
    }

    public void SetHoliday(DateTime date, string name)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO holidays (date, name) VALUES ($d, $n) ON CONFLICT(date) DO UPDATE SET name = excluded.name;";
        cmd.Parameters.AddWithValue("$d", date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$n", name);
        cmd.ExecuteNonQuery();
    }

    public void DeleteHoliday(DateTime date)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM holidays WHERE date = $d;";
        cmd.Parameters.AddWithValue("$d", date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        cmd.ExecuteNonQuery();
    }
}
