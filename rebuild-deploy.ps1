<#
.SYNOPSIS
    Rebuilds and deploys the Token Budget to Windows 11 Widgets Board.

.DESCRIPTION
    This script performs a full rebuild and deployment cycle:
    1. Increments the version number (for package identity changes)
    2. Unregisters any existing package
    3. Stops widget-related processes to release file locks
    4. Rebuilds the solution using Visual Studio
    5. Registers the new package with Windows

.PREREQUISITES
    - Visual Studio 2022 with "Windows application development" workload
    - Windows Developer Mode enabled
    - .NET 8 SDK

.EXAMPLE
    .\rebuild-deploy.ps1

.NOTES
    Run from the repository root directory.
    After deployment, press Win+W to open Widgets Board and add widgets.
#>

Write-Host "=== Rebuild and Deploy Token Budget ===" -ForegroundColor Cyan

# Increment version number and sync Add Widget display name
Write-Host "Incrementing version..." -ForegroundColor Yellow
$manifestPath = "$PSScriptRoot\packaging\TokenBudget.Package\Package.appxmanifest"
$content = Get-Content $manifestPath -Raw
if ($content -match '<Identity[^>]+Version="(\d+)\.(\d+)\.(\d+)\.(\d+)"') {
    $major = [int]$matches[1]; $minor = [int]$matches[2]; $build = [int]$matches[3] + 1; $revision = [int]$matches[4]
    $newVersion = "$major.$minor.$build.$revision"
    $content = $content -replace '<Identity([^>]+)Version="\d+\.\d+\.\d+\.\d+"', "<Identity`$1Version=""$newVersion"""
    $content = $content -replace '(DisplayName="Token Budget) v\d+(")', "`$1 v$build`$2"
    Set-Content -Path $manifestPath -Value $content -NoNewline
    Write-Host "Version updated to $newVersion, display name: Token Budget v$build" -ForegroundColor Green
} else {
    Write-Host "ERROR: Could not find version in manifest" -ForegroundColor Red
    exit 1
}

# Unregister old package first (prevents auto-restart)
Write-Host "Unregistering old package..." -ForegroundColor Yellow
$oldPkg = Get-AppxPackage | Where-Object { $_.Name -eq "TokenBudget" }
if ($oldPkg) {
    Remove-AppxPackage -Package $oldPkg.PackageFullName
    Write-Host "Old package unregistered" -ForegroundColor Green
}

# Kill widget provider, WidgetService, and WidgetBoard so cached metadata is cleared
Write-Host "Stopping widget processes..." -ForegroundColor Yellow
foreach ($procName in @("TokenBudget.App", "WidgetService", "WidgetBoard")) {
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

$remaining = Get-Process -Name "TokenBudget.App" -ErrorAction SilentlyContinue
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
& $devenv TokenBudget.sln /Rebuild "Debug|x64"

if ($LASTEXITCODE -eq 0) {
    Write-Host "Build succeeded!" -ForegroundColor Green

    # Register the app package
    Write-Host "`nRegistering package..." -ForegroundColor Yellow
    $manifestPath = "packaging\TokenBudget.Package\bin\x64\Debug\AppxManifest.xml"

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
