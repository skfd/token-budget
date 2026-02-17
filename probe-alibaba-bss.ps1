# Probe script: Test Alibaba Cloud BSS/Quota Center APIs with AccessKey credentials
# These APIs use HMAC-SHA1 signed requests (not bearer tokens)
# Tests: QueryBillOverview, ListProductQuotas

$ErrorActionPreference = "Stop"

# Read credentials
$credFile = "$env:USERPROFILE\.config\llm-token-widget\dashscope.json"
if (-not (Test-Path $credFile)) {
    Write-Host "ERROR: Credential file not found: $credFile" -ForegroundColor Red
    exit 1
}
$creds = Get-Content $credFile | ConvertFrom-Json
$accessKeyId = $creds.access_key_id
$accessKeySecret = $creds.access_key_secret
Write-Host "Loaded AccessKey ID: $accessKeyId"

function Sign-AlibabaRequest {
    param(
        [string]$Method,
        [hashtable]$Params,
        [string]$Secret
    )

    # Add common parameters
    $Params["Format"] = "JSON"
    $Params["SignatureMethod"] = "HMAC-SHA1"
    $Params["SignatureNonce"] = [guid]::NewGuid().ToString()
    $Params["SignatureVersion"] = "1.0"
    $Params["Timestamp"] = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
    $Params["AccessKeyId"] = $accessKeyId

    # Sort parameters by key
    $sorted = $Params.GetEnumerator() | Sort-Object Name

    # URL-encode each key and value
    $encoded = @()
    foreach ($kv in $sorted) {
        $k = [Uri]::EscapeDataString($kv.Name)
        $v = [Uri]::EscapeDataString($kv.Value)
        $encoded += "$k=$v"
    }
    $queryString = $encoded -join "&"

    # Build string to sign
    $stringToSign = "$Method&" + [Uri]::EscapeDataString("/") + "&" + [Uri]::EscapeDataString($queryString)

    # HMAC-SHA1
    $hmac = New-Object System.Security.Cryptography.HMACSHA1
    $hmac.Key = [Text.Encoding]::UTF8.GetBytes("$Secret&")
    $hash = $hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes($stringToSign))
    $signature = [Convert]::ToBase64String($hash)

    $Params["Signature"] = $signature
    return $Params
}

function Invoke-AlibabaApi {
    param(
        [string]$Host,
        [hashtable]$Params
    )

    $signed = Sign-AlibabaRequest -Method "GET" -Params $Params -Secret $accessKeySecret

    $queryParts = @()
    foreach ($kv in $signed.GetEnumerator()) {
        $k = [Uri]::EscapeDataString($kv.Name)
        $v = [Uri]::EscapeDataString($kv.Value)
        $queryParts += "$k=$v"
    }
    $url = "https://$Host/?" + ($queryParts -join "&")

    Write-Host "`nCalling: $Host | Action: $($Params['Action'])" -ForegroundColor Cyan
    try {
        $resp = Invoke-RestMethod -Uri $url -Method Get -TimeoutSec 15
        Write-Host "SUCCESS" -ForegroundColor Green
        $resp | ConvertTo-Json -Depth 10
    }
    catch {
        $status = $_.Exception.Response.StatusCode
        Write-Host "FAILED: $status" -ForegroundColor Red
        try {
            $reader = [System.IO.StreamReader]::new($_.Exception.Response.GetResponseStream())
            $body = $reader.ReadToEnd()
            Write-Host $body
        }
        catch {
            Write-Host $_.Exception.Message
        }
    }
}

Write-Host "`n=== BSS OpenAPI: QueryBillOverview ===" -ForegroundColor Yellow
$billingMonth = (Get-Date).ToString("yyyy-MM")
Invoke-AlibabaApi -Host "business.aliyuncs.com" -Params @{
    Action    = "QueryBillOverview"
    Version   = "2017-12-14"
    BillingCycle = $billingMonth
}

Write-Host "`n=== BSS OpenAPI: QueryInstanceBill ===" -ForegroundColor Yellow
Invoke-AlibabaApi -Host "business.aliyuncs.com" -Params @{
    Action    = "QueryInstanceBill"
    Version   = "2017-12-14"
    BillingCycle = $billingMonth
}

Write-Host "`n=== Quota Center: ListProductQuotas (DashScope) ===" -ForegroundColor Yellow
Invoke-AlibabaApi -Host "quotas.aliyuncs.com" -Params @{
    Action      = "ListProductQuotas"
    Version     = "2020-05-10"
    ProductCode = "dashscope"
}

Write-Host "`n=== Quota Center: ListProductQuotas (bailian) ===" -ForegroundColor Yellow
Invoke-AlibabaApi -Host "quotas.aliyuncs.com" -Params @{
    Action      = "ListProductQuotas"
    Version     = "2020-05-10"
    ProductCode = "bailian"
}

Write-Host "`n=== BSS OpenAPI: QueryAccountBalance ===" -ForegroundColor Yellow
Invoke-AlibabaApi -Host "business.aliyuncs.com" -Params @{
    Action  = "QueryAccountBalance"
    Version = "2017-12-14"
}

Write-Host "`nDone. Review output above to see which APIs return useful data." -ForegroundColor Yellow
