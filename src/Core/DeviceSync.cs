using GozcuStandalone.Device;
using GozcuStandalone.Storage;

namespace GozcuStandalone;

public sealed record SyncResult(string Serial, int Pending, IReadOnlyList<AttendanceRecord> Added, bool Acknowledged, bool ClockSet = false);

// One device's outcome within a multi-device sync pass.
public sealed record DeviceSyncResult(string DeviceId, string DeviceName, bool Ok, SyncResult? Result, string? Error);

// Talks to every enabled device in the devices table, one at a time, and stores what comes back.
// A single still-configured device behaves exactly as before; this is just that case generalized to many.
public sealed class DeviceSync
{
    // While a device's waiting count has not changed there is nothing new to download, and every
    // download keeps that terminal locked for a moment. Re-check fully now and then regardless.
    private static readonly TimeSpan ForceRefetchAfter = TimeSpan.FromMinutes(5);

    private readonly AppSettings _settings;
    private readonly AttendanceStore _store;
    private readonly object _gate = new();
    private readonly Dictionary<string, (int LastPending, DateTime LastFetch)> _perDevice = new();

    public DeviceSync(AppSettings settings, AttendanceStore store)
    {
        _settings = settings;
        _store = store;
    }

    // Syncs every enabled device in the devices table and saves each one's status. Returns one result per
    // device attempted, in the order configured. A device unreachable this time does not stop the others.
    public List<DeviceSyncResult> PullAll(bool acknowledge = false, bool trace = false, bool force = false)
    {
        lock (_gate)
        {
            var results = new List<DeviceSyncResult>();
            foreach (var device in _store.GetDevices().Where(d => d.Enabled))
            {
                try
                {
                    var r = PullOne(device, acknowledge, trace, force);
                    results.Add(new DeviceSyncResult(device.Id, device.Name, true, r, null));
                }
                catch (Exception ex)
                {
                    var msg = ex.GetBaseException().Message;
                    try { _store.SaveDeviceStatus(device.Id, new DeviceStatus(false, DateTime.Now, device.Serial, 0, msg)); } catch (Exception) { }
                    results.Add(new DeviceSyncResult(device.Id, device.Name, false, null, msg));
                }
            }
            return results;
        }
    }

    // Convenience for the common single-device setup and for callers that only care about one device.
    public DeviceSyncResult? PullOnce(string deviceId, bool acknowledge = false, bool trace = false, bool force = false)
    {
        var device = _store.GetDevice(deviceId);
        if (device is null) return null;
        lock (_gate)
        {
            try
            {
                var r = PullOne(device, acknowledge, trace, force);
                return new DeviceSyncResult(device.Id, device.Name, true, r, null);
            }
            catch (Exception ex)
            {
                var msg = ex.GetBaseException().Message;
                try { _store.SaveDeviceStatus(device.Id, new DeviceStatus(false, DateTime.Now, device.Serial, 0, msg)); } catch (Exception) { }
                return new DeviceSyncResult(device.Id, device.Name, false, null, msg);
            }
        }
    }

    private SyncResult PullOne(DeviceConfig device, bool acknowledge, bool trace, bool force)
    {
        using var dev = Open(device, trace);
        var serial = dev.GetSerial();
        if (serial != device.Serial) _store.SetDeviceSerial(device.Id, serial);
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

        var (lastPending, lastFetch) = _perDevice.TryGetValue(device.Id, out var s) ? s : (-1, DateTime.MinValue);

        SyncResult result;
        if (pending == 0)
        {
            _perDevice[device.Id] = (0, lastFetch);
            result = new SyncResult(serial, 0, Array.Empty<AttendanceRecord>(), false, clockSet);
        }
        else if (!force && !acknowledge && pending == lastPending && DateTime.UtcNow - lastFetch < ForceRefetchAfter)
        {
            result = new SyncResult(serial, pending, Array.Empty<AttendanceRecord>(), false, clockSet);
        }
        else
        {
            var records = dev.FetchPending(pending, closeWithoutConsuming: !acknowledge);
            var added = _store.Insert(records, serial);
            bool acked = false;
            if (acknowledge && records.Count > 0)
            {
                dev.Acknowledge(records.Count);
                acked = true;
            }
            _perDevice[device.Id] = (acked ? 0 : pending, DateTime.UtcNow);
            result = new SyncResult(serial, pending, added, acked, clockSet);
        }

        _store.SaveDeviceStatus(device.Id, new DeviceStatus(true, DateTime.Now, serial, result.Pending, ""));
        return result;
    }

    public bool SetClock(string deviceId, DateTime? when = null, bool trace = false)
    {
        var device = _store.GetDevice(deviceId) ?? throw new InvalidOperationException("Unknown device.");
        lock (_gate)
        {
            using var dev = Open(device, trace);
            return dev.SetTime(when ?? DateTime.Now);
        }
    }

    // Connects to a device and returns its serial and pending count without downloading anything,
    // for a "test connection" action when adding or editing a device.
    public static (string Serial, int Pending) TestConnect(string ip, int port, bool trace = false)
    {
        using var dev = new DeviceClient(ip, port) { Trace = trace };
        dev.Connect();
        dev.Handshake();
        return (dev.GetSerial(), dev.GetPendingCount());
    }

    private static DeviceClient Open(DeviceConfig device, bool trace)
    {
        if (string.IsNullOrWhiteSpace(device.Ip))
            throw new InvalidOperationException($"No address set for “{device.Name}”.");

        var dev = new DeviceClient(device.Ip, device.Port) { Trace = trace };
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
