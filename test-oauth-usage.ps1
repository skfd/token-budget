$credPath = Join-Path $env:USERPROFILE ".claude\.credentials.json"
if (-not (Test-Path $credPath)) {
    Write-Host "Credentials file not found at: $credPath"
    exit 1
}

$creds = Get-Content $credPath -Raw | ConvertFrom-Json
$token = $creds.claudeAiOauth.accessToken

if (-not $token) {
    Write-Host "No accessToken found in credentials. Keys available:"
    $creds | Get-Member -MemberType NoteProperty | ForEach-Object { Write-Host "  - $($_.Name)" }
    exit 1
}

Write-Host "Token found (first 20 chars): $($token.Substring(0, [Math]::Min(20, $token.Length)))..."
Write-Host ""
Write-Host "Calling https://api.anthropic.com/api/oauth/usage ..."

$headers = @{
    "Authorization"  = "Bearer $token"
    "Content-Type"   = "application/json"
    "anthropic-beta" = "oauth-2025-04-20"
    "User-Agent"     = "claude-code/2.0.37"
}

try {
    $response = Invoke-RestMethod -Uri "https://api.anthropic.com/api/oauth/usage" -Headers $headers -Method Get
    Write-Host "SUCCESS! Response:"
    $response | ConvertTo-Json -Depth 5
}
catch {
    Write-Host "FAILED: $($_.Exception.Message)"
    if ($_.Exception.Response) {
        $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
        $body = $reader.ReadToEnd()
        Write-Host "Response body: $body"
    }
}
