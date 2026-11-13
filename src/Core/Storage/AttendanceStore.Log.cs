using System.Globalization;
using GozcuStandalone.Device;

namespace GozcuStandalone.Storage;

public sealed record LogEntry(long Id, DateTime Time, string Level, string Area, string Message);

public sealed partial class AttendanceStore
{
    // Never throws: logging must not be able to break the thing being logged.
    public void Log(string level, string area, string message)
    {
        try
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO app_log (ts, level, area, message) VALUES ($t, $l, $a, $m);";
            cmd.Parameters.AddWithValue("$t", DateTime.Now.ToString(Fmt, CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("$l", level);
            cmd.Parameters.AddWithValue("$a", area);
            cmd.Parameters.AddWithValue("$m", message);
            cmd.ExecuteNonQuery();
        }
        catch (Exception) { }
    }

    public List<LogEntry> GetLog(DateTime from, DateTime toExclusive, string? level, string? text, int limit = 3000)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, ts, level, area, message FROM app_log
            WHERE ts >= $a AND ts < $b
              AND ($l IS NULL OR level = $l)
              AND ($q IS NULL OR message LIKE $p OR area LIKE $p)
            ORDER BY id DESC LIMIT $n;
            """;
        cmd.Parameters.AddWithValue("$a", from.ToString(Fmt, CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$b", toExclusive.ToString(Fmt, CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$l", string.IsNullOrEmpty(level) ? DBNull.Value : level);
        cmd.Parameters.AddWithValue("$q", string.IsNullOrWhiteSpace(text) ? DBNull.Value : text);
        cmd.Parameters.AddWithValue("$p", "%" + (text ?? "").Trim() + "%");
        cmd.Parameters.AddWithValue("$n", limit);

        var list = new List<LogEntry>();
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
            list.Add(new LogEntry(rd.GetInt64(0), DateTime.ParseExact(rd.GetString(1), Fmt, CultureInfo.InvariantCulture), rd.GetString(2), rd.GetString(3), rd.GetString(4)));
        return list;
    }

    // Manual entries and voided scans, newest first, with who changed them and when.
    public List<(AttendanceRecord Record, string? EditedBy, string? CreatedAt)> GetCorrections(DateTime from, DateTime toExclusive)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT {RecordCols}, edited_by, created_at FROM attendance_log
            WHERE (source = 'manual' OR voided = 1) AND timestamp >= $a AND timestamp < $b
            ORDER BY timestamp DESC, id DESC;
            """;
        cmd.Parameters.AddWithValue("$a", from.ToString(Fmt, CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$b", toExclusive.ToString(Fmt, CultureInfo.InvariantCulture));

        var list = new List<(AttendanceRecord, string?, string?)>();
        using var rd = cmd.ExecuteReader();
        while (rd.Read()) list.Add((MapRecord(rd), rd.IsDBNull(7) ? null : rd.GetString(7), rd.IsDBNull(8) ? null : rd.GetString(8)));
        return list;
    }

    public int PruneLog(int keepDays)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM app_log WHERE ts < $t;";
        cmd.Parameters.AddWithValue("$t", DateTime.Now.AddDays(-keepDays).ToString(Fmt, CultureInfo.InvariantCulture));
        return cmd.ExecuteNonQuery();
    }

    public void ClearLog()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM app_log;";
        cmd.ExecuteNonQuery();
    }
}
