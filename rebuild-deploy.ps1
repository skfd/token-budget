# Rebuild and Deploy Script for LLM Token Widget

Write-Host "=== Rebuild and Deploy LLM Token Widget ===" -ForegroundColor Cyan

# Increment version number
Write-Host "Incrementing version..." -ForegroundColor Yellow
& "$PSScriptRoot\increment-version.ps1"

# Unregister old package first (prevents auto-restart)
Write-Host "Unregistering old package..." -ForegroundColor Yellow
$oldPkg = Get-AppxPackage | Where-Object { $_.Name -eq "LlmTokenWidget" }
if ($oldPkg) {
    Remove-AppxPackage -Package $oldPkg.PackageFullName
    Write-Host "Old package unregistered" -ForegroundColor Green
}

# Kill widget provider, WidgetService, and WidgetBoard so cached metadata is cleared
Write-Host "Stopping widget processes..." -ForegroundColor Yellow
foreach ($procName in @("LlmTokenWidget.App", "WidgetService", "WidgetBoard")) {
    $maxAttempts = 5
    $attempt = 0
    while ($attempt -lt $maxAttempts) {
        $procs = Get-Process -Name $procName -ErrorAction SilentlyContinue
        if ($procs) {
            $procs | Stop-Process -Force -ErrorAction SilentlyContinue
            Start-Sleep -Milliseconds 500
            $attempt++
        } else {
            break
        }
    }
}

$remaining = Get-Process -Name "LlmTokenWidget.App" -ErrorAction SilentlyContinue
if ($remaining) {
    Write-Host "WARNING: Could not stop all widget processes. Build may fail." -ForegroundColor Yellow
} else {
    Write-Host "All widget processes stopped" -ForegroundColor Green
}

# Delay to ensure files are unlocked and services fully stopped
Start-Sleep -Seconds 2

# Find Visual Studio devenv.com
$devenv = "C:\Program Files\Microsoft Visual Studio\18\Insiders\Common7\IDE\devenv.com"

if (-not (Test-Path $devenv)) {
    $devenv = "C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\devenv.com"
}

if (-not (Test-Path $devenv)) {
    $devenv = "C:\Program Files\Microsoft Visual Studio\2022\Professional\Common7\IDE\devenv.com"
}

if (-not (Test-Path $devenv)) {
    $devenv = "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\Common7\IDE\devenv.com"
}

if (-not (Test-Path $devenv)) {
    Write-Host "ERROR: Visual Studio not found!" -ForegroundColor Red
    exit 1
}

Write-Host "Using Visual Studio at: $devenv" -ForegroundColor Green

# Rebuild
Write-Host "`nRebuilding solution..." -ForegroundColor Yellow
& $devenv LlmTokenWidget.sln /Rebuild "Debug|x64"

if ($LASTEXITCODE -eq 0) {
    Write-Host "Build succeeded!" -ForegroundColor Green

    # Register the app package
    Write-Host "`nRegistering package..." -ForegroundColor Yellow
    $manifestPath = "packaging\LlmTokenWidget.Package\bin\x64\Debug\AppxManifest.xml"

    if (Test-Path $manifestPath) {
        Add-AppxPackage -Path $manifestPath -Register
        Write-Host "`nDeploy succeeded!" -ForegroundColor Green
        Write-Host "`nNext: Try adding the widget from Win+W" -ForegroundColor Cyan
    } else {
        Write-Host "`nERROR: Package manifest not found at $manifestPath" -ForegroundColor Red
        exit 1
    }
} else {
    Write-Host "`nBuild failed!" -ForegroundColor Red
    exit 1
}
