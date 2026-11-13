using System.Text;
using GozcuStandalone.Device;

namespace GozcuStandalone.Reporting;

public static class AttendanceReport
{
    public static void WriteCsv(
        string path,
        IEnumerable<(AttendanceRecord Record, string Serial)> rows,
        IReadOnlyDictionary<uint, string> names)
    {
        var sb = new StringBuilder();
        sb.AppendLine("EnrollNumber,Name,Timestamp,Verify,VerifyMode,DeviceSerial");
        foreach (var (r, serial) in rows)
        {
            names.TryGetValue(r.EnrollNumber, out var name);
            sb.AppendLine($"{r.EnrollNumber},\"{(name ?? "").Replace("\"", "\"\"")}\",{r.Timestamp:yyyy-MM-dd HH:mm:ss},{r.VerifyName},{r.VerifyMode},{serial}");
        }
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
    }
}
