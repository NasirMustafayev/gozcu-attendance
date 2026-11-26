using System.Globalization;
using Microsoft.Data.Sqlite;

namespace GozcuStandalone.Storage;

// A terminal the app is configured to talk to. Id is a short slug used to key its status and its scans'
// device_serial-joined identity; Serial is filled in once the app has connected to it successfully.
public sealed record DeviceConfig(string Id, string Name, string Location, string Ip, int Port, bool Enabled, string Serial);

public sealed partial class AttendanceStore
{
    private static void EnsureDevicesTable(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS devices (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                location TEXT NOT NULL DEFAULT '',
                ip TEXT NOT NULL,
                port INTEGER NOT NULL,
                enabled INTEGER NOT NULL DEFAULT 1,
                serial TEXT NOT NULL DEFAULT '',
                sort_order INTEGER NOT NULL DEFAULT 0
            );
            """;
        cmd.ExecuteNonQuery();
    }

    public List<DeviceConfig> GetDevices()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name, location, ip, port, enabled, serial FROM devices ORDER BY sort_order, name;";
        var list = new List<DeviceConfig>();
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
            list.Add(new DeviceConfig(rd.GetString(0), rd.GetString(1), rd.GetString(2), rd.GetString(3),
                rd.GetInt32(4), rd.GetInt32(5) != 0, rd.GetString(6)));
        return list;
    }

    public DeviceConfig? GetDevice(string id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name, location, ip, port, enabled, serial FROM devices WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        using var rd = cmd.ExecuteReader();
        return rd.Read()
            ? new DeviceConfig(rd.GetString(0), rd.GetString(1), rd.GetString(2), rd.GetString(3), rd.GetInt32(4), rd.GetInt32(5) != 0, rd.GetString(6))
            : null;
    }

    // Creates or updates a device's configuration. Pass the existing id to edit it in place.
    public void SaveDevice(string id, string name, string location, string ip, int port, bool enabled)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO devices (id, name, location, ip, port, enabled, sort_order)
            VALUES ($id, $n, $l, $ip, $p, $e, (SELECT COALESCE(MAX(sort_order), 0) + 1 FROM devices))
            ON CONFLICT(id) DO UPDATE SET name = $n, location = $l, ip = $ip, port = $p, enabled = $e;
            """;
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$n", name);
        cmd.Parameters.AddWithValue("$l", location);
        cmd.Parameters.AddWithValue("$ip", ip);
        cmd.Parameters.AddWithValue("$p", port);
        cmd.Parameters.AddWithValue("$e", enabled ? 1 : 0);
        cmd.ExecuteNonQuery();
    }

    public void SetDeviceSerial(string id, string serial)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE devices SET serial = $s WHERE id = $id;";
        cmd.Parameters.AddWithValue("$s", serial);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public void DeleteDevice(string id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM devices WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
        SetState($"device_status:{id}", "");
    }

    // ---------- per-device live status (desktop app writes, web app reads) ----------

    public void SaveDeviceStatus(string deviceId, DeviceStatus s) =>
        SetState($"device_status:{deviceId}", string.Join('\t', s.Online ? "1" : "0",
            s.CheckedAt.ToString(Fmt, CultureInfo.InvariantCulture), s.Serial, s.Pending.ToString(CultureInfo.InvariantCulture), s.Error.Replace('\t', ' ')));

    public DeviceStatus? GetDeviceStatus(string deviceId)
    {
        var v = GetState($"device_status:{deviceId}")?.Split('\t');
        if (v is null || v.Length < 5) return null;
        return new DeviceStatus(v[0] == "1", DateTime.ParseExact(v[1], Fmt, CultureInfo.InvariantCulture), v[2],
            int.TryParse(v[3], out var p) ? p : 0, v[4]);
    }

    public Dictionary<string, DeviceStatus> GetAllDeviceStatuses(IEnumerable<string> deviceIds)
    {
        var map = new Dictionary<string, DeviceStatus>();
        foreach (var id in deviceIds)
            if (GetDeviceStatus(id) is { } s)
                map[id] = s;
        return map;
    }

    // One-time upgrade for databases created before multi-device support: turns the single device address
    // that used to live in settings.json into the first row of the devices table, carrying over its last
    // known status so the dashboard does not show "no device" right after upgrading. Safe to call every
    // startup: it only acts once, the first time the devices table is still empty.
    public const string LegacyDeviceId = "device-1";

    public void MigrateLegacyDevice(string ip, int port)
    {
        if (string.IsNullOrWhiteSpace(ip) || GetDevices().Count > 0) return;
        SaveDevice(LegacyDeviceId, "Terminal", "", ip, port, enabled: true);
        var legacyStatus = GetState("device_status")?.Split('\t');
        if (legacyStatus is { Length: >= 5 })
        {
            SetState($"device_status:{LegacyDeviceId}", GetState("device_status")!);
            if (legacyStatus[2].Length > 0) SetDeviceSerial(LegacyDeviceId, legacyStatus[2]);
        }
    }
}
