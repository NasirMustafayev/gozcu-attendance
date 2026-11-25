# Runs elevated: installs the Attendance web app as an always-on Windows service, opens port 5080 on
# private networks, keeps the PC from sleeping, and renews the reserved IP address. Output goes to a log file.
$log = Join-Path $PSScriptRoot "setup_web_service.log"
Start-Transcript -Path $log -Force | Out-Null
try {
    $exe = Join-Path $PSScriptRoot "dist\web\AttendanceWeb.exe"

    # A copy started by hand would hold port 5080.
    Get-Process AttendanceWeb -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 1

    if (Get-Service AttendanceWeb -ErrorAction SilentlyContinue) {
        Stop-Service AttendanceWeb -Force -ErrorAction SilentlyContinue
        sc.exe delete AttendanceWeb | Out-Host
        Start-Sleep -Seconds 2
    }
    New-Service -Name AttendanceWeb -DisplayName "Attendance Web" `
        -Description "Web version of the attendance app (http://<this PC>:5080)." `
        -BinaryPathName "`"$exe`"" -StartupType Automatic | Out-Host
    # Restart automatically if it ever stops unexpectedly.
    sc.exe failure AttendanceWeb reset= 86400 actions= restart/5000/restart/5000/restart/30000 | Out-Host
    Start-Service AttendanceWeb
    Get-Service AttendanceWeb | Format-List Name, Status, StartType | Out-Host

    Get-NetFirewallRule -DisplayName "Attendance Web (TCP 5080)" -ErrorAction SilentlyContinue | Remove-NetFirewallRule
    New-NetFirewallRule -DisplayName "Attendance Web (TCP 5080)" -Direction Inbound -Protocol TCP -LocalPort 5080 `
        -Action Allow -Profile Private, Domain | Format-List DisplayName, Enabled, Profile, Action | Out-Host

    # Never sleep or hibernate while plugged in; the screen may still turn off.
    powercfg /change standby-timeout-ac 0
    powercfg /change hibernate-timeout-ac 0
    "Sleep on AC: " + (powercfg /query SCHEME_CURRENT SUB_SLEEP STANDBYIDLE | Select-String "Current AC") | Out-Host

    # Pick up the router's reservation.
    ipconfig /renew Ethernet | Out-Null
    Get-NetIPAddress -InterfaceAlias Ethernet -AddressFamily IPv4 | Format-List IPAddress, PrefixOrigin | Out-Host
    "DONE"
}
catch { "ERROR: $_" }
finally { Stop-Transcript | Out-Null }
