$content = Get-Content "$env:TEMP\gradient_results.txt" -Raw
$map = @{}
foreach ($line in $content.Trim().Split("`n")) {
    $parts = $line.Split('=', 2)
    $map[$parts[0]] = $parts[1].Trim()
}

$cardsDir = "$PSScriptRoot\src\LlmTokenWidget.App\AdaptiveCards"

$providerMap = @{
    'claude'  = $map['CLAUDE']
    'zai'     = $map['ZAI']
    'copilot' = $map['COPILOT']
    'qwen'    = $map['QWEN']
}

foreach ($provider in $providerMap.Keys) {
    $newB64 = $providerMap[$provider]
    foreach ($size in 'small','medium','large') {
        $file = "$cardsDir\$provider-$size.json"
        $text = Get-Content $file -Raw
        $updated = $text -replace '"url": "data:image/png;base64,[^"]*"',
                                   "`"url`": `"data:image/png;base64,$newB64`""
        $utf8NoBom = New-Object System.Text.UTF8Encoding $false
        [System.IO.File]::WriteAllText($file, $updated, $utf8NoBom)
        Write-Host "Updated $provider-$size.json"
    }
}
Write-Host 'All done.' -ForegroundColor Green
