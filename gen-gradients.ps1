Add-Type -AssemblyName System.Drawing

function New-GradientBase64 {
    param(
        [int]$R, [int]$G, [int]$B,
        [int]$MaxAlpha = 30,
        [int]$Width = 600
    )
    $bmp = New-Object System.Drawing.Bitmap($Width, 1)
    for ($x = 0; $x -lt $Width; $x++) {
        $alpha = [int]($MaxAlpha * (1.0 - [double]$x / ($Width - 1)))
        $bmp.SetPixel($x, 0, [System.Drawing.Color]::FromArgb($alpha, $R, $G, $B))
    }
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $result = [Convert]::ToBase64String($ms.ToArray())
    $bmp.Dispose(); $ms.Dispose()
    return $result
}

# Brand colors (R, G, B) and max alpha (out of 255):
#   Claude/Anthropic: warm orange #CC7832, A=80
#   Z.ai (GLM):       deep blue   #3B6ACC, A=80
#   GitHub Copilot:   blue-indigo #5064DC, A=80
#   Qwen (Alibaba):   violet      #7950F2, A=80

Write-Host 'Claude  (#CC7832)...' -ForegroundColor Yellow
$claudeB64  = New-GradientBase64 -R 204 -G 120 -B 50  -MaxAlpha 80

Write-Host 'Z.ai    (#3B6ACC)...' -ForegroundColor Yellow
$zaiB64     = New-GradientBase64 -R 59  -G 106 -B 204 -MaxAlpha 80

Write-Host 'Copilot (#5064DC)...' -ForegroundColor Yellow
$copilotB64 = New-GradientBase64 -R 80  -G 100 -B 220 -MaxAlpha 80

Write-Host 'Qwen    (#7950F2)...' -ForegroundColor Yellow
$qwenB64    = New-GradientBase64 -R 121 -G 80  -B 242 -MaxAlpha 80

"CLAUDE=$claudeB64`nZAI=$zaiB64`nCOPILOT=$copilotB64`nQWEN=$qwenB64" |
    Out-File -FilePath "$env:TEMP\gradient_results.txt" -Encoding UTF8 -NoNewline

Write-Host 'Done.' -ForegroundColor Green
