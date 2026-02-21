$content = Get-Content "$env:TEMP\gradient_results.txt" -Raw
$lines = $content.Trim().Split("`n")
foreach ($line in $lines) {
    $parts = $line.Split('=', 2)
    Write-Host ("$($parts[0]): length=$($parts[1].Length)")
}
