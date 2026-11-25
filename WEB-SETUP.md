# Attendance web app — setup

The web app (`dist\web\AttendanceWeb.exe`) shows and edits the same data as the desktop app, in a browser.
It reads and writes the desktop app's database (`%LOCALAPPDATA%\GozcuStandalone\attendance.db`) and never talks
to the device: the desktop app keeps collecting scans, exactly as before.

Run both on the same office PC.

## 1. Update the desktop app first

The new desktop build (`dist\update\Attendance.exe`) reports the device status to the web app and lets both apps
use the database at the same time. Close the running desktop app (tray icon → Exit), copy
`dist\update\Attendance.exe` over `dist\Attendance.exe`, and start it again.

Also turn on **Settings → "Also copy new scans from the original software every 5 minutes"**. While the original
PDKS program is running it clears scans from the device, so without this setting the desktop app misses them.

## 2. Try it

Double-click `dist\web\AttendanceWeb.exe`, then open http://localhost:5080 on the same PC.
The first visit asks you to create the administrator account.
From other computers in the office use `http://<this PC's IP>:5080` (Windows asks once to allow it through the firewall).

## 3. Run it permanently (Windows service)

Edit `dist\web\appsettings.json` and set the database path, because a service runs under a different account:

```json
"DatabasePath": "C:\\Users\\<user>\\AppData\\Local\\GozcuStandalone\\attendance.db"
```

Then, in an administrator PowerShell:

```powershell
sc.exe create AttendanceWeb binPath= "<install folder>\GozcuStandalone\dist\web\AttendanceWeb.exe" start= auto
sc.exe start AttendanceWeb
```

## 4. Reach it from outside the office

Don't open port 5080 on the router: it is plain HTTP. Use one of these, both free for this size:

- **Cloudflare Tunnel** (public HTTPS address such as `attendance.yourcompany.az`, no open ports):
  install `cloudflared`, then `cloudflared tunnel --url http://localhost:5080` for a quick test, or a named tunnel
  bound to your domain for permanent use.
- **Tailscale** (private, only for people you invite): install Tailscale on this PC and on the managers' phones and
  laptops, then open `http://<tailscale name>:5080`.

## Roles

| Role   | Can |
|--------|-----|
| viewer | see everything, download reports |
| editor | also add/edit/void entries, change people, departments, shifts, leave and holidays |
| admin  | also manage web users |

Every change made on the web goes to the shared activity log with the user's name, e.g. `[web: anar]`.

## Sync from the web

The **Sync now** button on the web dashboard asks the desktop app to sync (it owns the device connection) and waits
for its answer, usually a few seconds. If the desktop app isn't running, the web app syncs with the device itself.
Either way it also copies new scans from PDKS when that setting is on.

## Command-line helpers (`src\Cli\bin\Debug\net8.0\gozcu.exe`)

- `gozcu request-sync` asks the running desktop app to sync, the same way the web button does
- `gozcu web-user list`, `gozcu web-user password <name> <new password>`, `gozcu web-user delete <name>`
- `gozcu db-check` checks the database for damage
