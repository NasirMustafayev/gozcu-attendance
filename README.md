# Attendance (Gozcu PDKS standalone)

An independent attendance system for the face and fingerprint terminal at `192.168.100.254:5005`, replacing the
Gozcu PDKS software. It talks to the terminal directly over its own TCP protocol (reverse-engineered from PDKS)
and keeps its data in a local SQLite database.

## Projects

| Folder | What it is |
|---|---|
| `src/Core` | Device protocol and sync, SQLite storage, PDKS import, shift and report logic, English/Azerbaijani text |
| `src/App` | Windows desktop app (WPF): collects scans from the device, dashboard, attendance, schedule, reports, people |
| `src/Web` | Web app (ASP.NET Core): the same screens in a browser, sign-in with roles, shares the desktop app's database |
| `src/Cli` | `gozcu` command-line tool: status, pull, reports, PDKS import, sync requests, web users, database check |

## Build

Requires the .NET 8 SDK.

```powershell
dotnet build src/App
dotnet publish src/App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o dist
dotnet publish src/Web -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o dist/web
```

`dotnet run --project src/Cli -- selftest` checks the device protocol code against captured traffic.

## Running

- The desktop app stores its data in `%LOCALAPPDATA%\GozcuStandalone` (`attendance.db`, `settings.json`, daily backups).
- Web app setup, running it as a Windows service and reaching it from other PCs: see [WEB-SETUP.md](WEB-SETUP.md).
- `setup-web-service.ps1` (run as administrator) installs the web app as a service, opens port 5080 on private
  networks and stops the PC from sleeping.

Reading from the device never removes scans from it unless "take over from the original software" is turned on,
so PDKS can keep running alongside. The PDKS database is only ever read.
