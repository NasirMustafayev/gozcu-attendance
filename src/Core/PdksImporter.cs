using GozcuStandalone.Device;
using GozcuStandalone.Storage;
using Microsoft.Data.SqlClient;

namespace GozcuStandalone;

public sealed record ImportResult(int RecordsRead, int RecordsAdded, int NamesAdded, int Shifts, int Assignments, int Leaves, int Departments);

// Copies scans, names, departments, shifts and leave out of the original software's database.
// Strictly read-only: it only runs SELECT statements against it, and it never overwrites what was changed here.
public static class PdksImporter
{
    // recentDays limits the scans read to the last few days (for the automatic import);
    // includeSchedule skips the large shift-day table, which rarely changes.
    public static ImportResult Import(AppSettings settings, AttendanceStore store, int? recentDays = null, bool includeSchedule = true)
    {
        var cs = new SqlConnectionStringBuilder
        {
            DataSource = settings.PdksServer,
            InitialCatalog = settings.PdksDatabase,
            IntegratedSecurity = true,
            TrustServerCertificate = true,
            Encrypt = SqlConnectionEncryptOption.Optional,
            ApplicationIntent = ApplicationIntent.ReadOnly,
            ConnectTimeout = 10,
        }.ConnectionString;

        using var conn = new SqlConnection(cs);
        conn.Open();

        var bySerial = new Dictionary<string, List<AttendanceRecord>>();
        int read = 0;
        var scanSql = "SELECT deviceEnrollNumber, verifyMode, attendanceDate, deviceSerialNumber FROM attendanceLog WHERE deviceEnrollNumber IS NOT NULL AND attendanceDate IS NOT NULL"
                      + (recentDays is null ? "" : " AND attendanceDate >= @since");
        using (var cmd = new SqlCommand(scanSql, conn) { CommandTimeout = 120 })
        {
            if (recentDays is { } days) cmd.Parameters.AddWithValue("@since", DateTime.Today.AddDays(-days));
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                var mode = rd.IsDBNull(1) ? 0 : rd.GetByte(1);
                var code = mode == 30 ? 5 : mode;
                var serial = rd.IsDBNull(3) ? "unknown" : rd.GetString(3).Trim();
                if (!bySerial.TryGetValue(serial, out var list)) bySerial[serial] = list = new();
                list.Add(new AttendanceRecord
                {
                    EnrollNumber = (uint)rd.GetInt32(0),
                    Timestamp = rd.GetDateTime(2),
                    RawFlags = (uint)(code << 4),
                });
                read++;
            }
        }

        int added = 0;
        foreach (var (serial, records) in bySerial)
            added += store.Insert(records, serial).Count;

        var names = new List<(uint, string)>();
        using (var cmd = new SqlCommand(
            "SELECT deviceEnrollNumber, LTRIM(RTRIM(ISNULL(firstName,'') + ' ' + ISNULL(lastName,''))) FROM employee WHERE ISNULL(deleted,0)=0 AND deviceEnrollNumber IS NOT NULL",
            conn))
        using (var rd = cmd.ExecuteReader())
        {
            while (rd.Read())
            {
                var name = rd.GetString(1);
                if (name.Length > 0) names.Add(((uint)rd.GetInt32(0), name));
            }
        }
        int namesAdded = store.AddMissingNames(names);

        // Employees deleted in the original software still own old scans: keep their names, as former staff.
        var known = store.GetNames();
        using (var cmd = new SqlCommand(
            "SELECT deviceEnrollNumber, LTRIM(RTRIM(ISNULL(firstName,'') + ' ' + ISNULL(lastName,''))) FROM employee WHERE ISNULL(deleted,0)=1 AND deviceEnrollNumber IS NOT NULL",
            conn))
        using (var rd = cmd.ExecuteReader())
        {
            while (rd.Read())
            {
                var id = (uint)rd.GetInt32(0);
                var name = rd.GetString(1);
                if (name.Length == 0 || known.ContainsKey(id)) continue;
                store.SetName(id, name);
                store.SetActive(id, false);
                known[id] = name;
                namesAdded++;
            }
        }

        // Every employee column except the device password, as an archive copy.
        if (recentDays is null)
        {
            var profile = new System.Data.DataTable();
            using (var cmd = new SqlCommand("SELECT * FROM employee", conn))
            using (var rd = cmd.ExecuteReader())
                profile.Load(rd);
            if (profile.Columns.Contains("devicePassword")) profile.Columns.Remove("devicePassword");
            store.SavePdksEmployees(profile);
        }

        var departments = new List<(int, string)>();
        using (var cmd = new SqlCommand("SELECT employeeGroupId, employeeGroupName FROM employeeGroup", conn))
        using (var rd = cmd.ExecuteReader())
        {
            while (rd.Read()) departments.Add((rd.GetInt32(0), rd.GetString(1).Trim()));
        }
        var members = new List<(uint, int)>();
        using (var cmd = new SqlCommand(
            "SELECT deviceEnrollNumber, employeeGroupId FROM employee WHERE ISNULL(deleted,0)=0 AND deviceEnrollNumber IS NOT NULL AND employeeGroupId IS NOT NULL",
            conn))
        using (var rd = cmd.ExecuteReader())
        {
            while (rd.Read()) members.Add(((uint)rd.GetInt32(0), rd.GetInt32(1)));
        }
        store.ImportDepartments(departments, members);

        var leaves = new List<LeaveEntry>();
        using (var cmd = new SqlCommand(
            "SELECT e.deviceEnrollNumber, l.startDate, l.endDate, ISNULL(l.description, ''), ISNULL(l.leaveType, 0) FROM [leave] l JOIN employee e ON e.employeeId = l.employeeId WHERE e.deviceEnrollNumber IS NOT NULL AND l.startDate IS NOT NULL AND l.endDate IS NOT NULL",
            conn))
        using (var rd = cmd.ExecuteReader())
        {
            while (rd.Read())
                leaves.Add(new LeaveEntry((uint)rd.GetInt32(0), rd.GetDateTime(1), rd.GetDateTime(2), rd.GetString(3).Trim(), Convert.ToInt32(rd.GetValue(4))));
        }
        store.SaveLeaves(leaves);

        int shiftCount = 0, assignmentCount = 0;
        if (includeSchedule)
        {
            static int Minutes(SqlDataReader rd, int i) => rd.IsDBNull(i) ? 0 : (int)rd.GetFieldValue<TimeSpan>(i).TotalMinutes;

            var shifts = new List<ShiftDef>();
            using (var cmd = new SqlCommand(
                "SELECT shiftId, shiftName, shiftStart, shiftEnd, earlyEnterTolerance, lateEnterTolerance, earlyLeaveTolerance, lateLeaveTolerance, daysOfWeek FROM shift",
                conn))
            using (var rd = cmd.ExecuteReader())
            {
                while (rd.Read())
                {
                    shifts.Add(new ShiftDef(
                        rd.GetInt32(0), rd.GetString(1),
                        rd.GetFieldValue<TimeSpan>(2), rd.GetFieldValue<TimeSpan>(3),
                        Minutes(rd, 4), Minutes(rd, 5), Minutes(rd, 6), Minutes(rd, 7),
                        rd.IsDBNull(8) ? 127 : rd.GetInt32(8)));
                }
            }

            var assignments = new List<(DateTime, uint, int)>();
            using (var cmd = new SqlCommand(
                "SELECT st.workDate, e.deviceEnrollNumber, st.shiftId FROM shiftTable st JOIN employee e ON e.employeeId = st.employeeId WHERE e.deviceEnrollNumber IS NOT NULL AND st.shiftId IS NOT NULL",
                conn) { CommandTimeout = 120 })
            using (var rd = cmd.ExecuteReader())
            {
                while (rd.Read()) assignments.Add((rd.GetDateTime(0), (uint)rd.GetInt32(1), rd.GetInt32(2)));
            }

            store.SaveShifts(shifts, assignments);
            shiftCount = shifts.Count;
            assignmentCount = assignments.Count;
        }

        return new ImportResult(read, added, namesAdded, shiftCount, assignmentCount, leaves.Count, departments.Count);
    }
}
