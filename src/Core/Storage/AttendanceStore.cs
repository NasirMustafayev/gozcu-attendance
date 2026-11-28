using System.Globalization;
using Microsoft.Data.Sqlite;
using GozcuStandalone.Device;

namespace GozcuStandalone.Storage;

public sealed partial class AttendanceStore
{
    private const string Fmt = "yyyy-MM-dd HH:mm:ss";
    private const string RecordCols = "id, enroll_number, timestamp, raw_flags, source, note, voided";

    private readonly string _connectionString;

    public string DatabasePath { get; }

    public AttendanceStore(string dbPath)
    {
        DatabasePath = dbPath;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            // Pooling can hand back a connection sitting inside an old, never-closed read
            // transaction, which then keeps seeing a stale snapshot of the database forever.
            // Every caller already opens and disposes its own connection per call, so there's
            // no performance reason to pool here, and this guarantees every read is current.
            Pooling = false,
        }.ToString();

        using var conn = Open();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS attendance_log (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    enroll_number INTEGER NOT NULL,
                    timestamp TEXT NOT NULL,
                    verify_mode INTEGER NOT NULL,
                    raw_flags INTEGER NOT NULL,
                    device_serial TEXT NOT NULL,
                    UNIQUE(enroll_number, timestamp, device_serial)
                );
                CREATE INDEX IF NOT EXISTS ix_attendance_timestamp ON attendance_log(timestamp);
                CREATE TABLE IF NOT EXISTS shifts (
                    shift_id INTEGER PRIMARY KEY,
                    name TEXT NOT NULL,
                    start_time TEXT NOT NULL,
                    end_time TEXT NOT NULL,
                    early_enter_min INTEGER NOT NULL,
                    late_enter_min INTEGER NOT NULL,
                    early_leave_min INTEGER NOT NULL,
                    late_leave_min INTEGER NOT NULL,
                    days_mask INTEGER NOT NULL
                );
                CREATE TABLE IF NOT EXISTS shift_assignments (
                    work_date TEXT NOT NULL,
                    enroll_number INTEGER NOT NULL,
                    shift_id INTEGER NOT NULL,
                    PRIMARY KEY (work_date, enroll_number)
                );
                CREATE TABLE IF NOT EXISTS employees (
                    enroll_number INTEGER PRIMARY KEY,
                    name TEXT NOT NULL,
                    active INTEGER NOT NULL DEFAULT 1
                );
                CREATE TABLE IF NOT EXISTS leaves (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    enroll_number INTEGER NOT NULL,
                    start_date TEXT NOT NULL,
                    end_date TEXT NOT NULL,
                    description TEXT NOT NULL,
                    leave_type INTEGER NOT NULL
                );
                CREATE TABLE IF NOT EXISTS holidays (
                    date TEXT PRIMARY KEY,
                    name TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS departments (
                    id INTEGER PRIMARY KEY,
                    name TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS always_present (
                    enroll_number INTEGER PRIMARY KEY,
                    enabled INTEGER NOT NULL DEFAULT 1,
                    first_in TEXT NOT NULL,
                    last_out TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS app_log (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ts TEXT NOT NULL,
                    level TEXT NOT NULL,
                    area TEXT NOT NULL,
                    message TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_app_log_ts ON app_log(ts);
                """;
            cmd.ExecuteNonQuery();
        }

        // Upgrades for databases created by earlier versions.
        EnsureColumn(conn, "employees", "active", "INTEGER NOT NULL DEFAULT 1");
        EnsureColumn(conn, "attendance_log", "source", "TEXT NOT NULL DEFAULT 'device'");
        EnsureColumn(conn, "attendance_log", "note", "TEXT");
        EnsureColumn(conn, "attendance_log", "voided", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(conn, "attendance_log", "edited_by", "TEXT");
        EnsureColumn(conn, "attendance_log", "created_at", "TEXT");
        EnsureColumn(conn, "employees", "department_id", "INTEGER");
        EnsureColumn(conn, "leaves", "source", "TEXT NOT NULL DEFAULT 'pdks'");
        EnsureColumn(conn, "shifts", "break_min", "INTEGER NOT NULL DEFAULT 0");
        EnsureWebTables(conn);
        EnsureDevicesTable(conn);

        // Classic rollback journal: both apps can still share the file (each waits up to 5 s for the other), and
        // the database stays one self-contained file. WAL mode was tried and is turned back off here.
        using var wal = conn.CreateCommand();
        wal.CommandText = "PRAGMA journal_mode = DELETE;";
        wal.ExecuteNonQuery();
    }

    private static void EnsureColumn(SqliteConnection conn, string table, string column, string ddl)
    {
        using var check = conn.CreateCommand();
        check.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = '{column}';";
        if (Convert.ToInt32(check.ExecuteScalar()) > 0) return;
        using var alter = conn.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {ddl};";
        alter.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var pragma = conn.CreateCommand();
        pragma.CommandText = "PRAGMA busy_timeout = 5000;";
        pragma.ExecuteNonQuery();
        return conn;
    }

    // ---------- scans ----------

    public List<AttendanceRecord> Insert(IEnumerable<AttendanceRecord> records, string deviceSerial)
    {
        var added = new List<AttendanceRecord>();
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT OR IGNORE INTO attendance_log (enroll_number, timestamp, verify_mode, raw_flags, device_serial, source, created_at)
            VALUES ($e, $t, $v, $f, $s, 'device', $c);
            """;
        var e = cmd.Parameters.Add("$e", SqliteType.Integer);
        var t = cmd.Parameters.Add("$t", SqliteType.Text);
        var v = cmd.Parameters.Add("$v", SqliteType.Integer);
        var f = cmd.Parameters.Add("$f", SqliteType.Integer);
        var s = cmd.Parameters.Add("$s", SqliteType.Text);
        var c = cmd.Parameters.Add("$c", SqliteType.Text);
        var now = DateTime.Now.ToString(Fmt, CultureInfo.InvariantCulture);

        foreach (var r in records)
        {
            e.Value = (long)r.EnrollNumber;
            t.Value = r.Timestamp.ToString(Fmt, CultureInfo.InvariantCulture);
            v.Value = r.VerifyMode;
            f.Value = (long)r.RawFlags;
            s.Value = deviceSerial;
            c.Value = now;
            if (cmd.ExecuteNonQuery() > 0) added.Add(r);
        }
        tx.Commit();
        return added;
    }

    public List<(AttendanceRecord Record, string Serial)> GetAll()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {RecordCols}, device_serial FROM attendance_log WHERE voided = 0 ORDER BY timestamp, id;";
        var list = new List<(AttendanceRecord, string)>();
        using var rd = cmd.ExecuteReader();
        while (rd.Read()) list.Add((MapRecord(rd), rd.GetString(7)));
        return list;
    }

    public List<AttendanceRecord> GetRange(DateTime from, DateTime toExclusive, bool includeVoided = false)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {RecordCols} FROM attendance_log WHERE timestamp >= $a AND timestamp < $b AND ($v = 1 OR voided = 0) ORDER BY timestamp, id;";
        cmd.Parameters.AddWithValue("$a", from.ToString(Fmt, CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$b", toExclusive.ToString(Fmt, CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$v", includeVoided ? 1 : 0);
        return ReadRecords(cmd);
    }

    public AttendanceRecord? GetScan(long id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {RecordCols} FROM attendance_log WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        return ReadRecords(cmd).FirstOrDefault();
    }

    public List<AttendanceRecord> GetLatest(int count)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {RecordCols} FROM attendance_log WHERE voided = 0 ORDER BY timestamp DESC, id DESC LIMIT $n;";
        cmd.Parameters.AddWithValue("$n", count);
        return ReadRecords(cmd);
    }

    public int Count()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM attendance_log WHERE voided = 0;";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static List<AttendanceRecord> ReadRecords(SqliteCommand cmd)
    {
        var list = new List<AttendanceRecord>();
        using var rd = cmd.ExecuteReader();
        while (rd.Read()) list.Add(MapRecord(rd));
        return list;
    }

    private static AttendanceRecord MapRecord(SqliteDataReader rd) => new()
    {
        Id = rd.GetInt64(0),
        EnrollNumber = (uint)rd.GetInt64(1),
        Timestamp = DateTime.ParseExact(rd.GetString(2), Fmt, CultureInfo.InvariantCulture),
        RawFlags = (uint)rd.GetInt64(3),
        Source = rd.GetString(4),
        Note = rd.IsDBNull(5) ? null : rd.GetString(5),
        Voided = rd.GetInt32(6) != 0,
    };

    // ---------- manual entries and corrections ----------

    // Adds a scan typed in by a person. Returns false if that exact entry already exists.
    public bool AddManual(uint enroll, DateTime when, string? note)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO attendance_log (enroll_number, timestamp, verify_mode, raw_flags, device_serial, source, note, edited_by, created_at)
            VALUES ($e, $t, 0, 0, 'manual', 'manual', $n, $u, $c);
            """;
        cmd.Parameters.AddWithValue("$e", (long)enroll);
        cmd.Parameters.AddWithValue("$t", when.ToString(Fmt, CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$n", (object?)note ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$u", Environment.UserName);
        cmd.Parameters.AddWithValue("$c", DateTime.Now.ToString(Fmt, CultureInfo.InvariantCulture));
        return cmd.ExecuteNonQuery() > 0;
    }

    // Fills a missing day with manual in/out scans at the shift's own start and end time, so the day counts
    // as a full day worked instead of absent. Returns false if entries for that day already exist.
    public bool MarkFull(uint enroll, DateTime date, ShiftDef shift, string by)
    {
        var inAdded = AddManual(enroll, date.Date + shift.Start, $"Marked full day by {by}");
        var outAdded = AddManual(enroll, date.Date + shift.End, $"Marked full day by {by}");
        return inAdded || outAdded;
    }

    // Admin-only: overwrites a scan's time in place, device scan or manual, with no void/replace trail.
    // Still fully logged by the caller; this just skips the audited void-and-replace CorrectScan uses.
    public bool OverwriteScan(long id, DateTime newWhen, string note, string by)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE attendance_log SET timestamp = $t, note = $n, edited_by = $u WHERE id = $id;";
        cmd.Parameters.AddWithValue("$t", newWhen.ToString(Fmt, CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$n", note);
        cmd.Parameters.AddWithValue("$u", by);
        cmd.Parameters.AddWithValue("$id", id);
        return cmd.ExecuteNonQuery() > 0;
    }

    // Changes the time of a scan. A manual entry is edited in place; a device scan is never altered:
    // it is voided and a manual entry at the corrected time replaces it, so both stay in the audit trail.
    public bool CorrectScan(long id, DateTime newWhen, string note)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();

        using var read = conn.CreateCommand();
        read.Transaction = tx;
        read.CommandText = "SELECT enroll_number, timestamp, source FROM attendance_log WHERE id = $id;";
        read.Parameters.AddWithValue("$id", id);
        long enroll; string oldTime, source;
        using (var rd = read.ExecuteReader())
        {
            if (!rd.Read()) return false;
            enroll = rd.GetInt64(0); oldTime = rd.GetString(1); source = rd.IsDBNull(2) ? "device" : rd.GetString(2);
        }

        var nowText = DateTime.Now.ToString(Fmt, CultureInfo.InvariantCulture);
        var newText = newWhen.ToString(Fmt, CultureInfo.InvariantCulture);
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.Parameters.AddWithValue("$e", enroll);
        cmd.Parameters.AddWithValue("$t", newText);
        cmd.Parameters.AddWithValue("$u", Environment.UserName);
        cmd.Parameters.AddWithValue("$c", nowText);
        cmd.Parameters.AddWithValue("$id", id);

        if (source == "manual")
        {
            cmd.Parameters.AddWithValue("$n", note);
            cmd.CommandText = "UPDATE OR IGNORE attendance_log SET timestamp = $t, note = $n, edited_by = $u WHERE id = $id;";
            if (cmd.ExecuteNonQuery() == 0) return false;
        }
        else
        {
            cmd.Parameters.AddWithValue("$n", $"Corrected from {oldTime[11..16]}: {note}");
            cmd.CommandText = """
                INSERT OR IGNORE INTO attendance_log (enroll_number, timestamp, verify_mode, raw_flags, device_serial, source, note, edited_by, created_at)
                VALUES ($e, $t, 0, 0, 'manual', 'manual', $n, $u, $c);
                """;
            if (cmd.ExecuteNonQuery() == 0) return false;
            cmd.CommandText = "UPDATE attendance_log SET voided = 1, note = $v, edited_by = $u WHERE id = $id;";
            cmd.Parameters.AddWithValue("$v", $"Replaced by {newText[11..16]}: {note}");
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
        return true;
    }

    // A voided scan stays in the database but is ignored by every report.
    public void SetVoided(long id, bool voided, string? note = null)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE attendance_log SET voided = $v, note = COALESCE($n, note), edited_by = $u WHERE id = $id;";
        cmd.Parameters.AddWithValue("$v", voided ? 1 : 0);
        cmd.Parameters.AddWithValue("$n", (object?)note ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$u", Environment.UserName);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public bool DeleteManual(long id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM attendance_log WHERE id = $id AND source = 'manual';";
        cmd.Parameters.AddWithValue("$id", id);
        return cmd.ExecuteNonQuery() > 0;
    }

    // ---------- people ----------

    public Dictionary<uint, string> GetNames()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT enroll_number, name FROM employees;";
        var map = new Dictionary<uint, string>();
        using var rd = cmd.ExecuteReader();
        while (rd.Read()) map[(uint)rd.GetInt64(0)] = rd.GetString(1);
        return map;
    }

    public List<(uint Enroll, string Name, bool Active)> GetPeople()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT enroll_number, name, active FROM employees;";
        var list = new List<(uint, string, bool)>();
        using var rd = cmd.ExecuteReader();
        while (rd.Read()) list.Add(((uint)rd.GetInt64(0), rd.GetString(1), rd.GetInt32(2) != 0));
        return list;
    }

    public void SetName(uint enroll, string name)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO employees (enroll_number, name) VALUES ($e, $n)
            ON CONFLICT(enroll_number) DO UPDATE SET name = excluded.name;
            """;
        cmd.Parameters.AddWithValue("$e", (long)enroll);
        cmd.Parameters.AddWithValue("$n", name);
        cmd.ExecuteNonQuery();
    }

    // A former employee keeps their name so old scans stay readable.
    public void SetActive(uint enroll, bool active)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE employees SET active = $a WHERE enroll_number = $e;";
        cmd.Parameters.AddWithValue("$a", active ? 1 : 0);
        cmd.Parameters.AddWithValue("$e", (long)enroll);
        cmd.ExecuteNonQuery();
    }

    public void DeleteName(uint enroll)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM employees WHERE enroll_number = $e;";
        cmd.Parameters.AddWithValue("$e", (long)enroll);
        cmd.ExecuteNonQuery();
    }

    public int AddMissingNames(IEnumerable<(uint Enroll, string Name)> names)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT OR IGNORE INTO employees (enroll_number, name) VALUES ($e, $n);";
        var e = cmd.Parameters.Add("$e", SqliteType.Integer);
        var n = cmd.Parameters.Add("$n", SqliteType.Text);
        int added = 0;
        foreach (var (enroll, name) in names)
        {
            e.Value = (long)enroll;
            n.Value = name;
            added += cmd.ExecuteNonQuery();
        }
        tx.Commit();
        return added;
    }

    // CSV lines of "enroll,name". Returns the number of names imported.
    public int ImportNames(string csvPath)
    {
        int n = 0;
        foreach (var raw in File.ReadAllLines(csvPath, System.Text.Encoding.UTF8))
        {
            var line = raw.Trim().TrimStart('﻿');
            int comma = line.IndexOf(',');
            if (comma <= 0 || !uint.TryParse(line[..comma].Trim(), out var enroll)) continue;
            var name = line[(comma + 1)..].Trim().Trim('"');
            if (name.Length == 0) continue;
            SetName(enroll, name);
            n++;
        }
        return n;
    }

    // IDs that scanned recently on the device but have no name here yet.
    public List<(uint Enroll, int Scans, DateTime Last)> GetUnnamedIds(int days)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT enroll_number, COUNT(*), MAX(timestamp) FROM attendance_log
            WHERE voided = 0 AND timestamp >= $since AND enroll_number NOT IN (SELECT enroll_number FROM employees)
            GROUP BY enroll_number ORDER BY MAX(timestamp) DESC;
            """;
        cmd.Parameters.AddWithValue("$since", DateTime.Today.AddDays(-days).ToString(Fmt, CultureInfo.InvariantCulture));
        var list = new List<(uint, int, DateTime)>();
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
            list.Add(((uint)rd.GetInt64(0), rd.GetInt32(1), DateTime.ParseExact(rd.GetString(2), Fmt, CultureInfo.InvariantCulture)));
        return list;
    }

    public uint NextFreeId()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT MAX(m) FROM (SELECT MAX(enroll_number) AS m FROM employees UNION ALL SELECT MAX(enroll_number) FROM attendance_log);";
        var v = cmd.ExecuteScalar();
        return v is null or DBNull ? 1u : (uint)Convert.ToInt64(v) + 1;
    }

    // ---------- maintenance ----------

    // Writes a consistent copy of the whole database, even while the app is using it.
    public void BackupTo(string path)
    {
        if (File.Exists(path)) File.Delete(path);
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"VACUUM INTO '{path.Replace("'", "''")}';";
        cmd.ExecuteNonQuery();
    }
}
