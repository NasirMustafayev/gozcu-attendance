namespace GozcuStandalone.Device;

// Wire format reverse-engineered from packet captures of the vendor app.
// Every frame ends with a little-endian 16-bit sum of all preceding bytes.
public static class Protocol
{
    public const ushort SessionKey = 0x1979;

    public const ushort CmdHello = 0x0052;
    public const ushort CmdStatusA = 0x010B;
    public const ushort CmdStatusB = 0x0108;
    public const ushort CmdPendingOrFetch = 0x0107;
    public const ushort CmdSerial = 0x0113;
    public const ushort CmdSetTime = 0x010F;

    // The original software brackets its device work with 0x010B ... 0x010C, and the terminal shows a
    // lock sign and ignores scans in between. Every session must end with the unlock.
    public const ushort CmdLock = 0x010B;
    public const ushort CmdUnlock = 0x010C;

    // 5A A5 01 00 + device time (seconds since 2000, local clock) + checksum.
    public static byte[] TimeFrame(DateTime local)
    {
        var p = new byte[10];
        p[0] = 0x5A; p[1] = 0xA5; p[2] = 0x01;
        BitConverter.GetBytes((uint)(local - Epoch).TotalSeconds).CopyTo(p, 4);
        WriteChecksum(p);
        return p;
    }

    public static readonly DateTime Epoch = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

    public static byte[] Request(ushort cmd, byte b8 = 0, byte b12 = 0)
    {
        var p = new byte[16];
        p[0] = 0x55; p[1] = 0xAA; p[2] = 0x01;
        p[4] = (byte)(SessionKey & 0xFF); p[5] = (byte)(SessionKey >> 8);
        p[6] = (byte)(cmd & 0xFF); p[7] = (byte)(cmd >> 8);
        p[8] = b8;
        p[12] = b12;
        WriteChecksum(p);
        return p;
    }

    public static byte[] Ack(uint count)
    {
        var p = new byte[10];
        p[0] = 0x5A; p[1] = 0xA5; p[2] = 0x01;
        BitConverter.GetBytes(count).CopyTo(p, 4);
        WriteChecksum(p);
        return p;
    }

    public static void WriteChecksum(byte[] frame)
    {
        int sum = 0;
        for (int i = 0; i < frame.Length - 2; i++) sum += frame[i];
        frame[^2] = (byte)(sum & 0xFF);
        frame[^1] = (byte)((sum >> 8) & 0xFF);
    }

    public static bool ChecksumOk(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 3) return false;
        int sum = 0;
        for (int i = 0; i < frame.Length - 2; i++) sum += frame[i];
        return frame[^2] == (byte)(sum & 0xFF) && frame[^1] == (byte)((sum >> 8) & 0xFF);
    }

    public static DateTime FromDeviceSeconds(uint seconds) => Epoch.AddSeconds(seconds);

    public static int IndexOf(byte[] data, ReadOnlySpan<byte> pattern)
    {
        for (int i = 0; i + pattern.Length <= data.Length; i++)
            if (data.AsSpan(i, pattern.Length).SequenceEqual(pattern)) return i;
        return -1;
    }
}
