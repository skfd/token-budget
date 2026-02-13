# Diagnostic script for widget errors

Write-Host "=== LLM Token Widget Diagnostics ===" -ForegroundColor Cyan

# Check if package is installed
Write-Host "`n1. Checking installed package..." -ForegroundColor Yellow
$package = Get-AppxPackage | Where-Object { $_.Name -like "*LlmToken*" }
if ($package) {
    Write-Host "✓ Package found:" -ForegroundColor Green
    Write-Host "  Name: $($package.Name)"
    Write-Host "  Version: $($package.Version)"
    Write-Host "  InstallLocation: $($package.InstallLocation)"
} else {
    Write-Host "✗ Package not found!" -ForegroundColor Red
}

# Check for recent errors in Event Viewer
Write-Host "`n2. Checking Event Viewer for widget errors..." -ForegroundColor Yellow
$errors = Get-WinEvent -LogName Application -MaxEvents 50 -ErrorAction SilentlyContinue |
    Where-Object {
        $_.Message -like "*Widget*" -or
        $_.Message -like "*LlmToken*" -or
        $_.Message -like "*9F910C81*" -or
        $_.ProviderName -eq "WidgetService"
    } | Select-Object -First 5

if ($errors) {
    Write-Host "Recent errors found:" -ForegroundColor Red
    foreach ($error in $errors) {
        Write-Host "`n--- Error at $($error.TimeCreated) ---" -ForegroundColor Yellow
        Write-Host $error.Message
    }
} else {
    Write-Host "No recent widget errors found" -ForegroundColor Green
}

# Check if executable exists
Write-Host "`n3. Checking executable..." -ForegroundColor Yellow
if ($package) {
    $exePath = Join-Path $package.InstallLocation "LlmTokenWidget.App.exe"
    if (Test-Path $exePath) {
        Write-Host "✓ Executable found: $exePath" -ForegroundColor Green

        # Try to run it manually
        Write-Host "`n4. Testing manual execution..." -ForegroundColor Yellow
        Write-Host "Starting widget provider manually (press Ctrl+C to stop)..." -ForegroundColor Yellow
        & $exePath
    } else {
        Write-Host "✗ Executable not found at: $exePath" -ForegroundColor Red
    }
}
