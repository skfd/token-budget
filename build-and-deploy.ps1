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

# Build the solution
Write-Host "Building solution..." -ForegroundColor Yellow
& $devenv LlmTokenWidget.sln /Build "Debug|x64"

if ($LASTEXITCODE -eq 0) {
    Write-Host "`n✓ Build succeeded!" -ForegroundColor Green

    # Deploy the package
    Write-Host "`nDeploying MSIX package..." -ForegroundColor Yellow
    & $devenv LlmTokenWidget.sln /Deploy "Debug|x64"

    if ($LASTEXITCODE -eq 0) {
        Write-Host "`n✓ Deploy succeeded!" -ForegroundColor Green
        Write-Host "`nNext steps:" -ForegroundColor Cyan
        Write-Host "1. Press Win+W to open Widgets Board" -ForegroundColor White
        Write-Host "2. Click the '+' button" -ForegroundColor White
        Write-Host "3. Search for 'Claude' or 'LLM'" -ForegroundColor White
        Write-Host "4. Add 'Claude Code Usage' widget" -ForegroundColor White
        Write-Host "`nYou should see the 'Hello Widget!' message!" -ForegroundColor Yellow
    } else {
        Write-Host "`n✗ Deploy failed with exit code $LASTEXITCODE" -ForegroundColor Red
        Write-Host "Try deploying manually from Visual Studio:" -ForegroundColor Yellow
        Write-Host "  - Open LlmTokenWidget.sln in Visual Studio" -ForegroundColor White
        Write-Host "  - Right-click LlmTokenWidget.Package → Deploy" -ForegroundColor White
    }
} else {
    Write-Host "`n✗ Build failed with exit code $LASTEXITCODE" -ForegroundColor Red
    Write-Host "Opening solution in Visual Studio for debugging..." -ForegroundColor Yellow
    & $devenv LlmTokenWidget.sln
}
