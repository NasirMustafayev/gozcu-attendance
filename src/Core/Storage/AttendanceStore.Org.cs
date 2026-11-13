using System.Globalization;
using Microsoft.Data.Sqlite;

namespace GozcuStandalone.Storage;

public sealed record DepartmentInfo(int Id, string Name);

public sealed record LeaveRow(long Id, uint Enroll, DateTime Start, DateTime End, string Description, int Type, string Source);

// Departments and leave entered in this app.
public sealed partial class AttendanceStore
{
    // Departments copied from the original software keep their ids (below 1000); new ones start at 1000.
    private const int LocalDepartmentIdStart = 1000;

    public List<DepartmentInfo> GetDepartments()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name FROM departments ORDER BY name COLLATE NOCASE;";
        var list = new List<DepartmentInfo>();
        using var rd = cmd.ExecuteReader();
        while (rd.Read()) list.Add(new DepartmentInfo(rd.GetInt32(0), rd.GetString(1)));
        return list;
    }

    public int AddDepartment(string name)
    {
        using var conn = Open();
        using var next = conn.CreateCommand();
        next.CommandText = "SELECT COALESCE(MAX(id), 0) FROM departments;";
        int id = Math.Max(LocalDepartmentIdStart, Convert.ToInt32(next.ExecuteScalar()) + 1);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO departments (id, name) VALUES ($i, $n);";
        cmd.Parameters.AddWithValue("$i", id);
        cmd.Parameters.AddWithValue("$n", name);
        cmd.ExecuteNonQuery();
        return id;
    }

    public void RenameDepartment(int id, string name)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE departments SET name = $n WHERE id = $i;";
        cmd.Parameters.AddWithValue("$n", name);
        cmd.Parameters.AddWithValue("$i", id);
        cmd.ExecuteNonQuery();
    }

    public void DeleteDepartment(int id)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE employees SET department_id = NULL WHERE department_id = $i; DELETE FROM departments WHERE id = $i;";
        cmd.Parameters.AddWithValue("$i", id);
        cmd.ExecuteNonQuery();
        tx.Commit();
    }

    public Dictionary<uint, int> GetPersonDepartments()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT enroll_number, department_id FROM employees WHERE department_id IS NOT NULL;";
        var map = new Dictionary<uint, int>();
        using var rd = cmd.ExecuteReader();
        while (rd.Read()) map[(uint)rd.GetInt64(0)] = rd.GetInt32(1);
        return map;
    }

    public void SetDepartment(IEnumerable<uint> people, int? departmentId)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE employees SET department_id = $d WHERE enroll_number = $e;";
        var d = cmd.Parameters.Add("$d", SqliteType.Integer);
        var e = cmd.Parameters.Add("$e", SqliteType.Integer);
        foreach (var person in people)
        {
            d.Value = departmentId is { } v ? v : DBNull.Value;
            e.Value = (long)person;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    // From the original software: adds departments that are missing and fills in the department of
    // people who have none. Anything already set or renamed here is left alone.
    public void ImportDepartments(IEnumerable<(int Id, string Name)> departments, IEnumerable<(uint Enroll, int DepartmentId)> members)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();

        foreach (var (id, name) in departments)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT OR IGNORE INTO departments (id, name) VALUES ($i, $n);";
            cmd.Parameters.AddWithValue("$i", id);
            cmd.Parameters.AddWithValue("$n", name);
            cmd.ExecuteNonQuery();
        }

        using var set = conn.CreateCommand();
        set.Transaction = tx;
        set.CommandText = "UPDATE employees SET department_id = $d WHERE enroll_number = $e AND department_id IS NULL AND EXISTS (SELECT 1 FROM departments WHERE id = $d);";
        var pd = set.Parameters.Add("$d", SqliteType.Integer);
        var pe = set.Parameters.Add("$e", SqliteType.Integer);
        foreach (var (enroll, departmentId) in members)
        {
            pd.Value = departmentId;
            pe.Value = (long)enroll;
            set.ExecuteNonQuery();
        }
        tx.Commit();
    }

    // ---------- leave entered here ----------

    public long AddLeave(uint enroll, DateTime start, DateTime end, string description, int type)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO leaves (enroll_number, start_date, end_date, description, leave_type, source)
            VALUES ($e, $s, $d, $n, $t, 'local');
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$e", (long)enroll);
        cmd.Parameters.AddWithValue("$s", start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$d", end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$n", description);
        cmd.Parameters.AddWithValue("$t", type);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    // Only leave entered here can be deleted; leave copied from the original software would just return on the next import.
    public bool DeleteLeave(long id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM leaves WHERE id = $i AND source = 'local';";
        cmd.Parameters.AddWithValue("$i", id);
        return cmd.ExecuteNonQuery() > 0;
    }

    // Leave that overlaps the given period, newest first.
    public List<LeaveRow> GetLeaves(DateTime from, DateTime to)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, enroll_number, start_date, end_date, description, leave_type, source FROM leaves
            WHERE end_date >= $a AND start_date <= $b ORDER BY start_date DESC, id DESC;
            """;
        cmd.Parameters.AddWithValue("$a", from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$b", to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        var list = new List<LeaveRow>();
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            list.Add(new LeaveRow(
                rd.GetInt64(0), (uint)rd.GetInt64(1),
                DateTime.ParseExact(rd.GetString(2), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                DateTime.ParseExact(rd.GetString(3), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                rd.GetString(4), rd.GetInt32(5), rd.GetString(6)));
        }
        return list;
    }
}
