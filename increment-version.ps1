# Increment version in manifest

$manifestPath = "packaging\LlmTokenWidget.Package\Package.appxmanifest"
$content = Get-Content $manifestPath -Raw

# Extract current version from Identity element only
if ($content -match '<Identity[^>]+Version="(\d+)\.(\d+)\.(\d+)\.(\d+)"') {
    $major = [int]$matches[1]
    $minor = [int]$matches[2]
    $build = [int]$matches[3]
    $revision = [int]$matches[4]

    # Increment build number
    $build++

    $newVersion = "$major.$minor.$build.$revision"

    # Only update the Identity Version, not MinVersion
    $content = $content -replace '<Identity([^>]+)Version="\d+\.\d+\.\d+\.\d+"', "<Identity`$1Version=""$newVersion"""

    # Update all relevant display names with build number
    # 1. Widget provider top-level DisplayName
    $content = $content -replace '(<uap3:AppExtension[^>]+)DisplayName="LLM Token Widget[^"]*"', "`$1DisplayName=""LLM Token Widget v$build"""

    # 2. Widget definition DisplayName
    $content = $content -replace 'DisplayName="Claude Code Usage v[\d.]+"', "DisplayName=""Claude Code Usage v$build"""
    $content = $content -replace '(<Definition Id="Claude_Usage_Widget" )DisplayName="Claude Code Usage"', "`$1DisplayName=""Claude Code Usage v$build"""

    Set-Content -Path $manifestPath -Value $content -NoNewline

    Write-Host "Version updated to $newVersion (build $build)" -ForegroundColor Green
} else {
    Write-Host "Could not find version in manifest" -ForegroundColor Red
}
