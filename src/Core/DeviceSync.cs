using GozcuStandalone.Device;
using GozcuStandalone.Storage;

namespace GozcuStandalone;

public sealed record SyncResult(string Serial, int Pending, IReadOnlyList<AttendanceRecord> Added, bool Acknowledged, bool ClockSet = false);

// One place that knows how to talk to the device and store what comes back.
public sealed class DeviceSync
{
    // While the device's waiting count has not changed there is nothing new to download, and every
    // download keeps the terminal locked for a moment. Re-check fully now and then regardless.
    private static readonly TimeSpan ForceRefetchAfter = TimeSpan.FromMinutes(5);

    private readonly AppSettings _settings;
    private readonly AttendanceStore _store;
    private readonly object _gate = new();
    private int _lastPending = -1;
    private DateTime _lastFetch = DateTime.MinValue;

    public DeviceSync(AppSettings settings, AttendanceStore store)
    {
        _settings = settings;
        _store = store;
    }

    public (string Serial, int Pending) ReadStatus(bool trace = false)
    {
        lock (_gate)
        {
            using var dev = Open(trace);
            return (dev.GetSerial(), dev.GetPendingCount());
        }
    }

    // Scans are saved locally first. Only after that, and only if asked, does the device
    // get told to drop them, so a failed save can never lose a scan.
    public SyncResult PullOnce(bool acknowledge = false, bool trace = false, bool force = false)
    {
        try
        {
            var r = Pull(acknowledge, trace, force);
            _store.SaveDeviceStatus(new DeviceStatus(true, DateTime.Now, r.Serial, r.Pending, ""));
            return r;
        }
        catch (Exception ex)
        {
            try { _store.SaveDeviceStatus(new DeviceStatus(false, DateTime.Now, "", 0, ex.GetBaseException().Message)); } catch (Exception) { }
            throw;
        }
    }

    private SyncResult Pull(bool acknowledge, bool trace, bool force)
    {
        lock (_gate)
        {
            using var dev = Open(trace);
            var serial = dev.GetSerial();
            var pending = dev.GetPendingCount();

            // Daily clock correction, done inside the same locked session as everything else.
            bool clockSet = false;
            if (_settings.AutoSetClock && _settings.LastClockSet?.Date != DateTime.Today)
            {
                try
                {
                    clockSet = dev.SetTime(DateTime.Now);
                    if (clockSet) { _settings.LastClockSet = DateTime.Now; _settings.Save(); }
                }
                catch (Exception) { /* the clock is a courtesy: a failure must not stop the scans */ }
            }

            if (pending == 0)
            {
                _lastPending = 0;
                return new SyncResult(serial, 0, Array.Empty<AttendanceRecord>(), false, clockSet);
            }

            if (!force && !acknowledge && pending == _lastPending && DateTime.UtcNow - _lastFetch < ForceRefetchAfter)
                return new SyncResult(serial, pending, Array.Empty<AttendanceRecord>(), false, clockSet);

            var records = dev.FetchPending(pending, closeWithoutConsuming: !acknowledge);
            var added = _store.Insert(records, serial);
            bool acked = false;
            if (acknowledge && records.Count > 0)
            {
                dev.Acknowledge(records.Count);
                acked = true;
            }

            _lastPending = acked ? 0 : pending;
            _lastFetch = DateTime.UtcNow;
            return new SyncResult(serial, pending, added, acked, clockSet);
        }
    }

    public bool SetClock(DateTime? when = null, bool trace = false)
    {
        lock (_gate)
        {
            using var dev = Open(trace);
            return dev.SetTime(when ?? DateTime.Now);
        }
    }

    private DeviceClient Open(bool trace)
    {
        if (string.IsNullOrWhiteSpace(_settings.DeviceIp))
            throw new InvalidOperationException("No device address set. Enter the terminal's IP address in Settings.");

        var dev = new DeviceClient(_settings.DeviceIp, _settings.DevicePort) { Trace = trace };
        try
        {
            dev.Connect();
            dev.Handshake();
            return dev;
        }
        catch
        {
            dev.Dispose();
            throw;
        }
    }
}
