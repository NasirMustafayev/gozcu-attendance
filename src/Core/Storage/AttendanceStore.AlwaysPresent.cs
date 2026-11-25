using System.Globalization;
using Microsoft.Data.Sqlite;

namespace GozcuStandalone.Storage;

public sealed partial class AttendanceStore
{
    public List<AlwaysPresentEntry> GetAlwaysPresent()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT enroll_number, enabled, first_in, last_out FROM always_present ORDER BY enroll_number;";
        var list = new List<AlwaysPresentEntry>();
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            list.Add(new AlwaysPresentEntry(
                (uint)rd.GetInt64(0), rd.GetInt32(1) != 0,
                TimeSpan.Parse(rd.GetString(2), CultureInfo.InvariantCulture),
                TimeSpan.Parse(rd.GetString(3), CultureInfo.InvariantCulture)));
        }
        return list;
    }

    public AlwaysPresentBook LoadAlwaysPresentBook() => new(GetAlwaysPresent());

    public void SetAlwaysPresent(uint enroll, bool enabled, TimeSpan firstIn, TimeSpan lastOut)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO always_present (enroll_number, enabled, first_in, last_out)
            VALUES ($e, $en, $fi, $lo)
            ON CONFLICT(enroll_number) DO UPDATE SET
                enabled = excluded.enabled, first_in = excluded.first_in, last_out = excluded.last_out;
            """;
        cmd.Parameters.AddWithValue("$e", (long)enroll);
        cmd.Parameters.AddWithValue("$en", enabled ? 1 : 0);
        cmd.Parameters.AddWithValue("$fi", firstIn.ToString(@"hh\:mm", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$lo", lastOut.ToString(@"hh\:mm", CultureInfo.InvariantCulture));
        cmd.ExecuteNonQuery();
    }

    public void RemoveAlwaysPresent(uint enroll)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM always_present WHERE enroll_number = $e;";
        cmd.Parameters.AddWithValue("$e", (long)enroll);
        cmd.ExecuteNonQuery();
    }
}
