# Write sample data to ~/.claude/widget-data.json to verify widget display
$path = "$env:USERPROFILE\.claude\widget-data.json"
$data = @{
    model = @{
        display_name = "Claude 3.5 Sonnet (Simulated)"
        id = "claude-3-5-sonnet-20240620"
    }
    cost = @{
        total_cost_usd = 1.23
        total_lines_added = 42
        total_lines_removed = 10
    }
    total_input_tokens = 150000
    total_output_tokens = 5000
    context_window = @{
        used_percentage = 45.5
        context_window_size = 200000
    }
    current_usage = @{
        input_tokens = 100
        output_tokens = 50
        cache_creation_input_tokens = 0
        cache_read_input_tokens = 2000
    }
    timestamp = (Get-Date).ToString("yyyy-MM-ddTHH:mm:ssZ")
}

$json = $data | ConvertTo-Json -Depth 5
Set-Content -Path $path -Value $json
Write-Host " wrote simulated data to $path"
Write-Host "Check widget for: Cost $1.23, Model 'Claude 3.5 Sonnet (Simulated)', Ctx: 45.5%"
