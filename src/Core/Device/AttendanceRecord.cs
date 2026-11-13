namespace GozcuStandalone.Device;

public sealed class AttendanceRecord
{
    public long Id { get; set; }
    public uint EnrollNumber { get; set; }
    public DateTime Timestamp { get; set; }
    public uint RawFlags { get; set; }

    // "device" for scans read from the terminal, "manual" for entries typed in by a person.
    public string Source { get; set; } = "device";
    public string? Note { get; set; }
    public bool Voided { get; set; }

    public bool IsManual => Source == "manual";

    // Flags bits 4-7: 1 = fingerprint, 5 = face. The PDKS database stores these as 1 and 30.
    public int VerifyCode => (int)((RawFlags >> 4) & 0xF);

    public int VerifyMode => VerifyCode switch
    {
        5 => 30,
        _ => VerifyCode,
    };

    public string VerifyName => IsManual ? Loc.T("Manual") : VerifyCode switch
    {
        1 => Loc.T("Fingerprint"),
        5 => Loc.T("Face"),
        _ => $"Unknown({VerifyCode})",
    };
}
