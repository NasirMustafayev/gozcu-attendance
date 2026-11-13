using System.Diagnostics;
using System.Net.Sockets;

namespace GozcuStandalone.Device;

public sealed class DeviceClient : IDisposable
{
    private static readonly byte[] StatusHeader = { 0xAA, 0x55, 0x01, 0x00, 0x00, 0x00, 0x01, 0x00 };
    private static readonly byte[] DataHeader = { 0xA5, 0x5A, 0x01, 0x00 };
    private static readonly byte[] BodyHeader = { 0x5A, 0xA5, 0x01, 0x00 };

    private readonly string _host;
    private readonly int _port;
    private TcpClient? _tcp;
    private NetworkStream? _stream;
    private bool _locked;

    public bool Trace { get; set; }

    public DeviceClient(string host, int port)
    {
        _host = host;
        _port = port;
    }

    public void Connect(int timeoutMs = 5000)
    {
        _tcp = new TcpClient { NoDelay = true };
        if (!_tcp.ConnectAsync(_host, _port).Wait(timeoutMs))
            throw new DeviceProtocolException($"Timed out connecting to {_host}:{_port}");
        _stream = _tcp.GetStream();
    }

    // Releases the terminal so it accepts scans again. Never throws: it also runs when a session failed halfway.
    public void Unlock()
    {
        if (!_locked || _stream is null || _tcp is null) return;
        _locked = false;
        try
        {
            Send(Protocol.Request(Protocol.CmdUnlock));
            Receive(500, 100, 22);
        }
        catch (Exception) { }
    }

    public void Dispose()
    {
        Unlock();
        _stream?.Dispose();
        _tcp?.Dispose();
        _stream = null;
        _tcp = null;
    }

    // Same opening sequence the vendor app uses, minus the clock-set step.
    public void Handshake()
    {
        Exchange(Protocol.Request(Protocol.CmdHello));
        Exchange(Protocol.Request(Protocol.CmdHello, b12: 1));
        _locked = true;
        Exchange(Protocol.Request(Protocol.CmdLock));
        Exchange(Protocol.Request(Protocol.CmdStatusB, b12: 6));
        Receive(400, 100); // the device can follow with an extra pending-count frame; drop it so replies stay in step
    }

    public string GetSerial()
    {
        // If a stray frame is still in flight the first answer is not the serial; asking again gets it.
        for (int attempt = 0; attempt < 3; attempt++)
        {
            var r = Exchange(Protocol.Request(Protocol.CmdSerial), 60);
            int i = Protocol.IndexOf(r, DataHeader);
            if (i >= 0 && r.Length >= i + 4 + 32) return System.Text.Encoding.ASCII.GetString(r, i + 4, 32).TrimEnd('\0');
        }
        throw new DeviceProtocolException("No serial number in reply.");
    }

    public int GetPendingCount()
    {
        var r = Exchange(Protocol.Request(Protocol.CmdPendingOrFetch));
        int i = Protocol.IndexOf(r, StatusHeader);
        if (i < 0 || r.Length < i + 14 || !Protocol.ChecksumOk(r.AsSpan(i, 14)))
            throw new DeviceProtocolException("No valid pending-count reply.");
        return (int)BitConverter.ToUInt32(r, i + 8);
    }

    // Reads the pending scans without removing them from the device.
    public List<AttendanceRecord> FetchPending(int count, bool closeWithoutConsuming = true)
    {
        if (count <= 0) return new();

        Send(Protocol.Request(Protocol.CmdPendingOrFetch, (byte)Math.Min(count, 255), 1));
        var buf = new List<byte>(Receive(1500, 150, 8));
        Send(Protocol.Ack(1));
        for (int attempt = 0; attempt < 6; attempt++)
        {
            buf.AddRange(Receive(1500, 150, ExpectedDownloadBytes(Math.Min(count, 255)) + 8 - buf.Count));
            bool parsed;
            List<AttendanceRecord> records;
            try { parsed = TryParseRecords(buf.ToArray(), out records); }
            catch (DeviceProtocolException) { AbortTransfer(); throw; }
            if (parsed)
            {
                // The terminal keeps showing its lock sign, and ignores the unlock, until the download is closed.
                // Telling it "received 0 records" closes it without removing anything from its queue.
                if (closeWithoutConsuming)
                {
                    Send(Protocol.Ack(0));
                    Receive(500, 100, 14);
                }
                return records;
            }
        }
        AbortTransfer();
        throw new DeviceProtocolException("Device did not return a complete record frame.");
    }

    // Closes a download that was cut short, without removing anything from the device's queue.
    public void AbortTransfer()
    {
        try
        {
            Send(Protocol.Ack(0));
            Receive(800, 150);
        }
        catch (Exception) { /* best effort */ }
    }

    // Tells the device these scans were received, which removes them from its pending queue.
    // Anything else reading the same queue (the original software) will no longer see them.
    public void Acknowledge(int count)
    {
        if (count <= 0) return;
        Send(Protocol.Ack((uint)count));
        Receive(1500, 150, 14);
    }

    // Sets the device clock, as the original software does on every connection.
    public bool SetTime(DateTime localTime)
    {
        Send(Protocol.Request(Protocol.CmdSetTime, 4));
        var reply = new List<byte>(Receive(1500, 150, 8));
        Send(Protocol.TimeFrame(localTime));
        reply.AddRange(Receive(1500, 150, 14));
        return Protocol.IndexOf(reply.ToArray(), StatusHeader) >= 0;
    }

    public static bool TryParseRecords(byte[] data, out List<AttendanceRecord> records)
    {
        records = new();
        int i = Protocol.IndexOf(data, DataHeader);
        if (i < 0 || data.Length < i + 14) return false;
        if (!Protocol.ChecksumOk(data.AsSpan(i, 14))) return false;

        int n = (int)BitConverter.ToUInt32(data, i + 4);
        int pos = i + 14;

        // The body is sent in chunks of at most 1024 bytes (a 4-byte header, up to 85 records, a 2-byte checksum),
        // each chunk with its own header and checksum.
        int left = n;
        while (left > 0)
        {
            int take = Math.Min(left, MaxRecordsPerChunk);
            int chunkLen = 4 + 12 * take + 2;
            if (data.Length < pos + chunkLen) { records.Clear(); return false; }
            if (!data.AsSpan(pos, 4).SequenceEqual(BodyHeader)) { records.Clear(); return false; }
            if (!Protocol.ChecksumOk(data.AsSpan(pos, chunkLen)))
                throw new DeviceProtocolException("Record frame checksum mismatch.");

            for (int k = 0; k < take; k++)
            {
                int o = pos + 4 + 12 * k;
                records.Add(new AttendanceRecord
                {
                    Timestamp = Protocol.FromDeviceSeconds(BitConverter.ToUInt32(data, o)),
                    EnrollNumber = BitConverter.ToUInt32(data, o + 4),
                    RawFlags = BitConverter.ToUInt32(data, o + 8),
                });
            }
            pos += chunkLen;
            left -= take;
        }
        return true;
    }

    private const int MaxRecordsPerChunk = 85;

    // Bytes the device sends for a download of this many records, after the 8-byte ack.
    private static int ExpectedDownloadBytes(int count) =>
        14 + 6 * ((count + MaxRecordsPerChunk - 1) / MaxRecordsPerChunk) + 12 * count;

    // Most requests are answered with an 8-byte ack followed by a 14-byte status frame.
    public byte[] Exchange(byte[] request, int expectedBytes = 22)
    {
        Send(request);
        return Receive(1500, 150, expectedBytes);
    }

    private void Send(byte[] data)
    {
        if (_stream is null) throw new DeviceProtocolException("Not connected.");
        if (Trace) Console.WriteLine($"C> {Convert.ToHexString(data)}");
        _stream.Write(data, 0, data.Length);
    }

    // Returns as soon as minBytes have arrived (plus a short grace period), otherwise after
    // the device has been quiet for idleMs.
    private byte[] Receive(int firstWaitMs, int idleMs, int minBytes = 0)
    {
        if (_stream is null || _tcp is null) throw new DeviceProtocolException("Not connected.");
        var ms = new MemoryStream();
        var buf = new byte[4096];
        int wait = firstWaitMs;
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 10000 && _tcp.Client.Poll(wait * 1000, SelectMode.SelectRead))
        {
            int n = _stream.Read(buf, 0, buf.Length);
            if (n == 0) throw new DeviceProtocolException("Device closed the connection.");
            ms.Write(buf, 0, n);
            wait = minBytes > 0 ? (ms.Length >= minBytes ? 15 : 600) : idleMs;
        }
        var result = ms.ToArray();
        if (Trace && result.Length > 0) Console.WriteLine($"D> {Convert.ToHexString(result)}");
        return result;
    }
}
