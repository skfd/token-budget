# Check for widget-related errors in Event Viewer
Write-Host "=== Widget Diagnostic Logs ===" -ForegroundColor Cyan

# Check package
Write-Host "`n--- Installed Package ---" -ForegroundColor Yellow
Get-AppxPackage | Where-Object { $_.Name -eq "LlmTokenWidget" } | Format-List Name, Version, InstallLocation

# Check recent Application log entries
Write-Host "--- Recent Application Errors ---" -ForegroundColor Yellow
$events = Get-WinEvent -LogName Application -MaxEvents 100 -ErrorAction SilentlyContinue |
    Where-Object {
        $_.Message -match "Widget" -or
        $_.Message -match "LlmToken" -or
        $_.Message -match "9F910C81" -or
        $_.Message -match "COM"
    } | Select-Object -First 10

if ($events) {
    foreach ($evt in $events) {
        Write-Host "`n[$($evt.TimeCreated)] Level: $($evt.LevelDisplayName)" -ForegroundColor Yellow
        Write-Host $evt.Message
        Write-Host "---"
    }
} else {
    Write-Host "No widget-related errors found in Application log"
}

# Also check System log
Write-Host "`n--- Recent System Errors ---" -ForegroundColor Yellow
$sysEvents = Get-WinEvent -LogName System -MaxEvents 100 -ErrorAction SilentlyContinue |
    Where-Object {
        $_.Message -match "Widget" -or
        $_.Message -match "LlmToken" -or
        $_.Message -match "9F910C81"
    } | Select-Object -First 5

if ($sysEvents) {
    foreach ($evt in $sysEvents) {
        Write-Host "`n[$($evt.TimeCreated)] Level: $($evt.LevelDisplayName)" -ForegroundColor Yellow
        Write-Host $evt.Message
        Write-Host "---"
    }
} else {
    Write-Host "No widget-related errors found in System log"
}

# Check if widget process is running
Write-Host "`n--- Widget Process Status ---" -ForegroundColor Yellow
$proc = Get-Process -Name "LlmTokenWidget.App" -ErrorAction SilentlyContinue
if ($proc) {
    Write-Host "Widget provider is RUNNING (PID: $($proc.Id))" -ForegroundColor Green
} else {
    Write-Host "Widget provider is NOT running" -ForegroundColor Red
}
