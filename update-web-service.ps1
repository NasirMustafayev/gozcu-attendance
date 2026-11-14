# Run as administrator. Stops the Attendance Web service, swaps in the newly published build
# (dist\web_new), keeps the old one as dist\web_old for rollback, and starts the service again.
$log = Join-Path $PSScriptRoot "update_web_service.log"
Start-Transcript -Path $log -Force | Out-Null
try {
    $root = $PSScriptRoot
    $live = Join-Path $root "dist\web"
    $new = Join-Path $root "dist\web_new"
    $old = Join-Path $root "dist\web_old"

    Stop-Service AttendanceWeb -Force
    Start-Sleep -Seconds 2

    if (Test-Path $old) { Remove-Item $old -Recurse -Force }
    Rename-Item $live $old
    Rename-Item $new $live

    Start-Service AttendanceWeb
    Get-Service AttendanceWeb | Format-List Name, Status, StartType | Out-Host
    "DONE"
}
catch { "ERROR: $_" }
finally { Stop-Transcript | Out-Null }
