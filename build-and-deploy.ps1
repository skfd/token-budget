# Build and Deploy Script for LLM Token Widget
# Run this after Visual Studio workload installation completes

Write-Host "=== LLM Token Widget - Build and Deploy ===" -ForegroundColor Cyan

# Find Visual Studio
$devenv = "C:\Program Files\Microsoft Visual Studio\18\Insiders\Common7\IDE\devenv.com"

if (-not (Test-Path $devenv)) {
    $devenv = "C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\devenv.com"
}

if (-not (Test-Path $devenv)) {
    $devenv = "C:\Program Files\Microsoft Visual Studio\2022\Professional\Common7\IDE\devenv.com"
}

if (-not (Test-Path $devenv)) {
    Write-Error "Visual Studio not found. Please ensure Visual Studio 2022 is installed."
    exit 1
}

Write-Host "Using Visual Studio: $devenv`n" -ForegroundColor Green

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

# Find MSBuild for package restoration
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

# Restore packages before building
Write-Host "`nRestoring NuGet packages..." -ForegroundColor Yellow
& $msbuild LlmTokenWidget.sln /t:Restore /p:Configuration=Debug /p:Platform=x64 /v:minimal
if ($LASTEXITCODE -ne 0) {
    Write-Error "Package restore failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

# Build the solution
Write-Host "`nBuilding solution..." -ForegroundColor Yellow
& $devenv LlmTokenWidget.sln /Build "Debug|x64"

if ($LASTEXITCODE -eq 0) {
    Write-Host "`nOK: Build succeeded!" -ForegroundColor Green

    # Deploy the package
    Write-Host "`nDeploying MSIX package..." -ForegroundColor Yellow
    & $devenv LlmTokenWidget.sln /Deploy "Debug|x64"

    if ($LASTEXITCODE -eq 0) {
        Write-Host "`nOK: Deploy succeeded!" -ForegroundColor Green
        Write-Host "`nNext steps:" -ForegroundColor Cyan
        Write-Host "1. Press Win+W to open Widgets Board" -ForegroundColor White
        Write-Host "2. Click the '+' button" -ForegroundColor White
        Write-Host "3. Search for 'Claude' or 'LLM'" -ForegroundColor White
        Write-Host "4. Add 'Claude Code Usage' widget" -ForegroundColor White
        Write-Host "`nYou should see the 'Hello Widget!' message!" -ForegroundColor Yellow
    } else {
        Write-Host "`nERROR: Deploy failed with exit code $LASTEXITCODE" -ForegroundColor Red
        Write-Host "Try deploying manually from Visual Studio:" -ForegroundColor Yellow
        Write-Host "  - Open LlmTokenWidget.sln in Visual Studio" -ForegroundColor White
        Write-Host "  - Right-click LlmTokenWidget.Package -> Deploy" -ForegroundColor White
    }
} else {
    Write-Host "`nERROR: Build failed with exit code $LASTEXITCODE" -ForegroundColor Red
}
