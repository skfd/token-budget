<#
.SYNOPSIS
    Builds the Token Budget widget for Microsoft Store Publication.

.DESCRIPTION
    This script performs a Release build configured for the Microsoft Store.
    It produces an unsigned .msixupload file that can be uploaded directly
    to the Microsoft Partner Center.

.PREREQUISITES
    - Visual Studio 2022 with "Windows application development" workload
    - .NET 8 SDK

.EXAMPLE
    .\build-store.ps1
#>

Write-Host "=== Build Token Budget for Microsoft Store ===" -ForegroundColor Cyan

# Find Visual Studio msbuild.exe
$msbuild = "C:\Program Files\Microsoft Visual Studio\18\Insiders\MSBuild\Current\Bin\MSBuild.exe"

if (-not (Test-Path $msbuild)) {
    $msbuild = "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"
}

if (-not (Test-Path $msbuild)) {
    $msbuild = "C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe"
}

if (-not (Test-Path $msbuild)) {
    $msbuild = "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe"
}

if (-not (Test-Path $msbuild)) {
    Write-Host "ERROR: MSBuild not found! Make sure Visual Studio 2022 is installed." -ForegroundColor Red
    exit 1
}

Write-Host "Using MSBuild at: $msbuild" -ForegroundColor Green

# 1. Restore NuGet packages
Write-Host "`nRestoring NuGet packages..." -ForegroundColor Yellow
& $msbuild TokenBudget.sln /t:Restore /p:Configuration=Release /p:Platform=x64 /p:RuntimeIdentifier=win-x64

if ($LASTEXITCODE -ne 0) {
    Write-Host "`nRestore failed!" -ForegroundColor Red
    exit 1
}

# 2. Build for Store Upload
Write-Host "`nBuilding Store package (.msixupload)..." -ForegroundColor Yellow
& $msbuild packaging\TokenBudget.Package\TokenBudget.Package.wapproj `
    /p:Configuration=Release /p:Platform=x64 `
    /p:RuntimeIdentifier=win-x64 `
    /p:UapAppxPackageBuildMode=StoreUpload `
    /p:AppxBundle=Always `
    /p:AppxPackageSigningEnabled=false `
    /p:GenerateAppxPackageOnBuild=true

if ($LASTEXITCODE -eq 0) {
    Write-Host "`nBuild succeeded!" -ForegroundColor Green
    
    # Locate the .msixupload file
    $uploadFiles = Get-ChildItem -Path "packaging\TokenBudget.Package\AppPackages\*" -Include "*.msixupload" -Recurse
    
    if ($uploadFiles) {
        $latestUpload = $uploadFiles | Sort-Object LastWriteTime -Descending | Select-Object -First 1
        Write-Host "`n========================================================" -ForegroundColor Cyan
        Write-Host "SUCCESS! Your Store upload package is ready at:" -ForegroundColor Green
        Write-Host $latestUpload.FullName -ForegroundColor White
        Write-Host "Upload this file directly to the Microsoft Partner Center." -ForegroundColor Yellow
        Write-Host "========================================================" -ForegroundColor Cyan
    } else {
        Write-Host "`nWARNING: Build succeeded, but could not find the .msixupload file." -ForegroundColor Yellow
    }
} else {
    Write-Host "`nBuild failed!" -ForegroundColor Red
    exit 1
}
