# Build script for LLM Token Widget using Visual Studio MSBuild

param(
    [string]$Configuration = "Debug",
    [string]$Platform = "x64",
    [switch]$Deploy
)

# Find MSBuild
$msbuildPaths = @(
    "C:\Program Files\Microsoft Visual Studio\18\Insiders\MSBuild\Current\Bin\amd64\MSBuild.exe",
    "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\amd64\MSBuild.exe",
    "C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\amd64\MSBuild.exe",
    "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\amd64\MSBuild.exe"
)

$msbuild = $null
foreach ($path in $msbuildPaths) {
    if (Test-Path $path) {
        $msbuild = $path
        Write-Host "Found MSBuild: $msbuild" -ForegroundColor Green
        break
    }
}

if (-not $msbuild) {
    Write-Error "MSBuild not found. Please install Visual Studio 2022 with Windows application development workload."
    exit 1
}

Write-Host "`n=== Building LLM Token Widget ===" -ForegroundColor Cyan
Write-Host "Configuration: $Configuration" -ForegroundColor Yellow
Write-Host "Platform: $Platform`n" -ForegroundColor Yellow

# Kill any running instances of the widget app that might lock files
Write-Host "Checking for running instances of LlmTokenWidget.App..." -ForegroundColor Yellow
$lockedProcesses = Get-Process | Where-Object { $_.ProcessName -eq "LlmTokenWidget.App" -or $_.ProcessName -eq "LlmTokenWidget" }

if ($lockedProcesses) {
    foreach ($proc in $lockedProcesses) {
        Write-Host "  Found process: $($proc.ProcessName) (PID: $($proc.Id))" -ForegroundColor Cyan
        try {
            Stop-Process -Id $proc.Id -Force -ErrorAction Stop
            Write-Host "  Killed process: $($proc.ProcessName) (PID: $($proc.Id))" -ForegroundColor Green
        } catch {
            Write-Host "  Failed to kill process: $($proc.ProcessName) (PID: $($proc.Id))" -ForegroundColor Red
            Write-Host "  Error: $($_.Exception.Message)" -ForegroundColor Red
        }
    }
} else {
    Write-Host "  No running instances found." -ForegroundColor Green
}

Write-Host ""

# Restore packages
Write-Host "Restoring NuGet packages..." -ForegroundColor Cyan
& $msbuild LlmTokenWidget.sln /t:Restore /p:Configuration=$Configuration /p:Platform=$Platform /v:minimal
if ($LASTEXITCODE -ne 0) {
    Write-Error "Restore failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

# Build solution
Write-Host "`nBuilding solution..." -ForegroundColor Cyan
& $msbuild LlmTokenWidget.sln /t:Build /p:Configuration=$Configuration /p:Platform=$Platform /v:minimal
if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

Write-Host "`nOK Build succeeded!" -ForegroundColor Green

# Deploy if requested
if ($Deploy) {
    Write-Host "`nDeploying MSIX package..." -ForegroundColor Cyan
    & $msbuild packaging\LlmTokenWidget.Package\LlmTokenWidget.Package.wapproj /t:Deploy /p:Configuration=$Configuration /p:Platform=$Platform /v:minimal
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Deploy failed with exit code $LASTEXITCODE"
        exit $LASTEXITCODE
    }
    Write-Host "`nOK Deploy succeeded!" -ForegroundColor Green
    Write-Host "`nPress Win+W to open Widgets Board and add the widget." -ForegroundColor Yellow
}
else {
    Write-Host "`nTo deploy, run: .\build.ps1 -Deploy" -ForegroundColor Yellow
    Write-Host "Or use Visual Studio: Right-click LlmTokenWidget.Package -> Deploy" -ForegroundColor Yellow
}
