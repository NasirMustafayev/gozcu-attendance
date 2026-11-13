using System.Data;
using Microsoft.Data.Sqlite;

namespace GozcuStandalone.Storage;

// Full copy of the original software's employee records (title, contact details, hire/exit dates, photo and so on).
// It is a mirror: every import refreshes it, and nothing in this app edits it.
public sealed partial class AttendanceStore
{
    public int SavePdksEmployees(DataTable table)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();

        var cols = table.Columns.Cast<DataColumn>().ToList();
        using (var create = conn.CreateCommand())
        {
            create.Transaction = tx;
            create.CommandText = "CREATE TABLE IF NOT EXISTS pdks_employee (" + string.Join(", ", cols.Select(c => $"\"{c.ColumnName}\"" + (c.ColumnName == "employeeId" ? " INTEGER PRIMARY KEY" : ""))) + ");";
            create.ExecuteNonQuery();
        }

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"INSERT OR REPLACE INTO pdks_employee ({string.Join(", ", cols.Select(c => $"\"{c.ColumnName}\""))}) VALUES ({string.Join(", ", cols.Select((_, i) => "$p" + i))});";
        var ps = cols.Select((_, i) => cmd.Parameters.Add("$p" + i, SqliteType.Text)).ToList();

        foreach (DataRow row in table.Rows)
        {
            for (int i = 0; i < cols.Count; i++)
            {
                var v = row[i];
                ps[i].Value = v switch
                {
                    DBNull => DBNull.Value,
                    DateTime d => d.ToString("yyyy-MM-dd HH:mm:ss"),
                    decimal m => m.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    _ => v,
                };
            }
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
        return table.Rows.Count;
    }
}
