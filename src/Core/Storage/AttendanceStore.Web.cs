using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace GozcuStandalone.Storage;

// What the desktop app last saw on the device, so the web app (which never talks to the device) can show it.
public sealed record DeviceStatus(bool Online, DateTime CheckedAt, string Serial, int Pending, string Error);

// Pages is empty for "every page the role allows"; otherwise a comma-separated allowlist of page keys (see WebApp.Pages).
public sealed record WebUser(string Username, string DisplayName, string Role, string Language, DateTime Created, string Pages = "");

// Shared state and sign-in accounts for the web app.
public sealed partial class AttendanceStore
{
    public const string RoleAdmin = "admin";
    public const string RoleEditor = "editor";
    public const string RoleViewer = "viewer";

    private static void EnsureWebTables(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS app_state (key TEXT PRIMARY KEY, value TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS web_users (
                username TEXT PRIMARY KEY COLLATE NOCASE,
                display_name TEXT NOT NULL,
                password_hash TEXT NOT NULL,
                role TEXT NOT NULL,
                language TEXT NOT NULL DEFAULT 'en',
                created_at TEXT NOT NULL,
                pages TEXT NOT NULL DEFAULT ''
            );
            """;
        cmd.ExecuteNonQuery();

        using var check = conn.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM pragma_table_info('web_users') WHERE name = 'pages';";
        if (Convert.ToInt32(check.ExecuteScalar()) == 0)
        {
            using var alter = conn.CreateCommand();
            alter.CommandText = "ALTER TABLE web_users ADD COLUMN pages TEXT NOT NULL DEFAULT '';";
            alter.ExecuteNonQuery();
        }
    }

    public string? GetState(string key)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM app_state WHERE key = $k;";
        cmd.Parameters.AddWithValue("$k", key);
        return cmd.ExecuteScalar() as string;
    }

    public void SetState(string key, string value)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO app_state (key, value) VALUES ($k, $v) ON CONFLICT(key) DO UPDATE SET value = excluded.value;";
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", value);
        cmd.ExecuteNonQuery();
    }

    // ---------- sync requests from the web app ----------

    // The web app asks; the desktop app (which owns the device connection) does the sync and answers.
    public long RequestSync(string by)
    {
        var id = DateTime.UtcNow.Ticks;
        SetState("sync_request", $"{id}\t{by}");
        return id;
    }

    public (long Id, string By)? GetSyncRequest()
    {
        var v = GetState("sync_request")?.Split('\t');
        return v is { Length: 2 } && long.TryParse(v[0], out var id) ? (id, v[1]) : null;
    }

    // Keyed by request id (not a single shared slot) so one page's wait for its own request is never
    // mistaken for another: two tabs, or two clicks in a row, must each get their own answer back.
    public void SaveSyncResult(long id, bool ok, int added, string message) =>
        SetState($"sync_result:{id}", $"{(ok ? 1 : 0)}\t{added}\t{message.Replace('\t', ' ')}");

    public (bool Ok, int Added, string Message)? GetSyncResult(long id)
    {
        var v = GetState($"sync_result:{id}")?.Split('\t');
        return v is { Length: 3 } ? (v[0] == "1", int.TryParse(v[1], out var n) ? n : 0, v[2]) : null;
    }

    public void ClearSyncResult(long id) => SetState($"sync_result:{id}", "");

    // ---------- web accounts ----------

    public int CountWebUsers()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM web_users;";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public List<WebUser> GetWebUsers()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT username, display_name, role, language, created_at, pages FROM web_users ORDER BY username;";
        var list = new List<WebUser>();
        using var rd = cmd.ExecuteReader();
        while (rd.Read()) list.Add(ReadUser(rd));
        return list;
    }

    public WebUser? GetWebUser(string username)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT username, display_name, role, language, created_at, pages FROM web_users WHERE username = $u;";
        cmd.Parameters.AddWithValue("$u", username.Trim());
        using var rd = cmd.ExecuteReader();
        return rd.Read() ? ReadUser(rd) : null;
    }

    public WebUser? CheckWebLogin(string username, string password)
    {
        string? stored;
        using (var conn = Open())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT password_hash FROM web_users WHERE username = $u;";
            cmd.Parameters.AddWithValue("$u", username.Trim());
            stored = cmd.ExecuteScalar() as string;
        }
        return stored is not null && VerifyPassword(password, stored) ? GetWebUser(username) : null;
    }

    // Creates the account, or updates it; a null password keeps the current one.
    // pages is empty for "every page the role allows", else a comma-separated allowlist of page keys.
    public void SaveWebUser(string username, string displayName, string role, string language, string? password, string pages = "")
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.Parameters.AddWithValue("$u", username.Trim());
        cmd.Parameters.AddWithValue("$d", displayName.Trim());
        cmd.Parameters.AddWithValue("$r", role);
        cmd.Parameters.AddWithValue("$l", language);
        cmd.Parameters.AddWithValue("$g", pages);
        cmd.Parameters.AddWithValue("$c", DateTime.Now.ToString(Fmt, CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$p", password is null ? "" : HashPassword(password));
        cmd.CommandText = password is null
            ? "UPDATE web_users SET display_name = $d, role = $r, language = $l, pages = $g WHERE username = $u;"
            : """
              INSERT INTO web_users (username, display_name, password_hash, role, language, created_at, pages) VALUES ($u, $d, $p, $r, $l, $c, $g)
              ON CONFLICT(username) DO UPDATE SET display_name = $d, password_hash = $p, role = $r, language = $l, pages = $g;
              """;
        cmd.ExecuteNonQuery();
    }

    public void SetWebUserLanguage(string username, string language)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE web_users SET language = $l WHERE username = $u;";
        cmd.Parameters.AddWithValue("$u", username);
        cmd.Parameters.AddWithValue("$l", language);
        cmd.ExecuteNonQuery();
    }

    public void DeleteWebUser(string username)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM web_users WHERE username = $u;";
        cmd.Parameters.AddWithValue("$u", username);
        cmd.ExecuteNonQuery();
    }

    private static WebUser ReadUser(SqliteDataReader rd) =>
        new(rd.GetString(0), rd.GetString(1), rd.GetString(2), rd.GetString(3), DateTime.ParseExact(rd.GetString(4), Fmt, CultureInfo.InvariantCulture), rd.GetString(5));

    // PBKDF2-SHA256 with a random salt.
    private static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 210_000, HashAlgorithmName.SHA256, 32);
        return $"pbkdf2${210_000}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    private static bool VerifyPassword(string password, string stored)
    {
        var parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != "pbkdf2" || !int.TryParse(parts[1], out var rounds)) return false;
        var salt = Convert.FromBase64String(parts[2]);
        var expected = Convert.FromBase64String(parts[3]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, rounds, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
