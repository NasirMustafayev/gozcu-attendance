using GozcuStandalone.Device;

namespace GozcuStandalone;

// Checks our packet builders and parser against bytes captured from the original software.
public static class SelfTest
{
    public static List<string> Run()
    {
        var failures = new List<string>();

        void Expect(string name, string actual, string expected)
        {
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                failures.Add($"{name}: got {actual}, expected {expected}");
        }

        Expect("hello", Convert.ToHexString(Protocol.Request(Protocol.CmdHello)), "55AA010079195200000000000000E401");
        Expect("hello 2", Convert.ToHexString(Protocol.Request(Protocol.CmdHello, b12: 1)), "55AA010079195200000000000100E501");
        Expect("serial", Convert.ToHexString(Protocol.Request(Protocol.CmdSerial)), "55AA010079191301000000000000A601");
        Expect("pending", Convert.ToHexString(Protocol.Request(Protocol.CmdPendingOrFetch)), "55AA0100791907010000000000009A01");
        Expect("fetch 1", Convert.ToHexString(Protocol.Request(Protocol.CmdPendingOrFetch, 1, 1)), "55AA0100791907010100000001009C01");
        Expect("fetch 2", Convert.ToHexString(Protocol.Request(Protocol.CmdPendingOrFetch, 2, 1)), "55AA0100791907010200000001009D01");
        Expect("set time request", Convert.ToHexString(Protocol.Request(Protocol.CmdSetTime, 4)), "55AA010079190F01040000000000A601");
        Expect("unlock", Convert.ToHexString(Protocol.Request(Protocol.CmdUnlock)), "55AA010079190C010000000000009F01");
        Expect("lock", Convert.ToHexString(Protocol.Request(Protocol.CmdLock)), "55AA010079190B010000000000009E01");
        Expect("ack 1",Convert.ToHexString(Protocol.Ack(1)), "5AA50100010000000101");
        Expect("ack 2", Convert.ToHexString(Protocol.Ack(2)), "5AA50100020000000201");
        Expect("close download", Convert.ToHexString(Protocol.Ack(0)), "5AA50100000000000001");
        Expect("time frame", Convert.ToHexString(Protocol.TimeFrame(new DateTime(2026, 11, 2, 11, 54, 52))), "5AA501008C367B326F02");

        var frame = Convert.FromHexString("A55A0100020000000000000002015AA50100F8347B32230000005000060066367B320100000050800D007905");
        if (!DeviceClient.TryParseRecords(frame, out var records) || records.Count != 2)
        {
            failures.Add("record frame did not parse into 2 records");
        }
        else
        {
            Expect("record 1", $"{records[0].Timestamp:yyyy-MM-dd HH:mm:ss} #{records[0].EnrollNumber} {records[0].VerifyName}", "2026-11-02 11:48:08 #35 Face");
            Expect("record 2", $"{records[1].Timestamp:yyyy-MM-dd HH:mm:ss} #{records[1].EnrollNumber} {records[1].VerifyName}", "2026-11-02 11:54:14 #1 Face");
        }

        // Unpaid break: taken off a full day but not a short visit.
        var shift = new ShiftDef(1, "Test", new TimeSpan(9, 0, 0), new TimeSpan(18, 0, 0), 0, 5, 0, 15, 127, BreakMinutes: 60);
        var day = new DateTime(2026, 10, 5);
        var book = new ShiftBook(new[] { shift }, new Dictionary<(DateTime, uint), int> { [(day, 1u)] = 1, [(day, 2u)] = 1 });
        var scans = new[]
        {
            new Device.AttendanceRecord { EnrollNumber = 1, Timestamp = day.AddHours(9) },
            new Device.AttendanceRecord { EnrollNumber = 1, Timestamp = day.AddHours(18) },
            new Device.AttendanceRecord { EnrollNumber = 2, Timestamp = day.AddHours(9) },
            new Device.AttendanceRecord { EnrollNumber = 2, Timestamp = day.AddHours(12) },
        };
        var rows = Reporting.DailyReport.Build(scans, new Dictionary<uint, string>(), new TimeSpan(9, 0, 0), new TimeSpan(18, 0, 0), book);
        Expect("break taken off a full day", rows.First(r => r.Enroll == 1).Worked.ToString(), "08:00:00");
        Expect("break not taken off a short visit", rows.First(r => r.Enroll == 2).Worked.ToString(), "03:00:00");

        // Excel file: a real zip with the expected parts and well-formed sheet XML.
        var xlsx = Path.Combine(Path.GetTempPath(), $"selftest-{Guid.NewGuid():N}.xlsx");
        try
        {
            var table = new Reporting.ReportTable { Title = "Self test", Headers = { "ID", "Name", "Note" } };
            table.Rows.Add(new[] { "7", "Ə & <Ö>", "ok" });
            table.Rows.Add(new[] { "008", "Ünal", "" });
            table.WriteXlsx(xlsx);
            using var zip = System.IO.Compression.ZipFile.OpenRead(xlsx);
            foreach (var part in new[] { "[Content_Types].xml", "xl/workbook.xml", "xl/worksheets/sheet1.xml", "xl/styles.xml" })
            {
                var entry = zip.GetEntry(part);
                if (entry is null) { failures.Add("xlsx is missing " + part); continue; }
                using var s = entry.Open();
                try { System.Xml.Linq.XDocument.Load(s); }
                catch (System.Xml.XmlException ex) { failures.Add($"xlsx part {part} is not valid XML: {ex.Message}"); }
            }
            using var sheet = zip.GetEntry("xl/worksheets/sheet1.xml")!.Open();
            var text = new StreamReader(sheet).ReadToEnd();
            if (!text.Contains("Ə &amp; &lt;Ö&gt;")) failures.Add("xlsx did not escape or keep special characters");
            if (!text.Contains("<v>7</v>") || text.Contains("<v>008</v>")) failures.Add("xlsx number handling is wrong");
        }
        finally { try { File.Delete(xlsx); } catch (IOException) { } }

        return failures;
    }
}
