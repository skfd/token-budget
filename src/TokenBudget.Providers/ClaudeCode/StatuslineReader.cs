using System.Text.Json;
using TokenBudget.Core;

namespace TokenBudget.Providers.ClaudeCode;

/// <summary>
/// Reads real-time statusline data from ~/.claude/widget-data.json.
/// This file is populated by the statusline.sh script.
/// </summary>
public sealed class StatuslineReader
{
    private readonly string _statusFilePath;

    public StatuslineReader()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _statusFilePath = Path.Combine(userProfile, ".claude", "widget-data.json");
    }

    public StatuslineData? Read()
    {
        if (!File.Exists(_statusFilePath))
            return null;

        // Check file age — if > 5 minutes old, Claude Code probably isn't running
        var lastWrite = File.GetLastWriteTimeUtc(_statusFilePath);
        if (DateTime.UtcNow - lastWrite > TimeSpan.FromMinutes(5))
            return null;

        try
        {
            // Use FileShare.ReadWrite to avoid locking conflicts with the script writing it
            using var stream = new FileStream(_statusFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            
            // Allow reading empty files (race condition protection)
            if (stream.Length == 0) return null;

            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;

            string? model = null;
            if (root.TryGetProperty("model", out var modelProp))
            {
                // Try display_name first, then id
                if (modelProp.TryGetProperty("display_name", out var dn)) model = dn.GetString();
                if (string.IsNullOrEmpty(model) && modelProp.TryGetProperty("id", out var id)) model = id.GetString();
            }

            double? cost = null;
            if (root.TryGetProperty("cost", out var costProp) && 
                costProp.TryGetProperty("total_cost_usd", out var costVal) && 
                costVal.ValueKind == JsonValueKind.Number)
            {
                cost = costVal.GetDouble();
            }

            double? contextUsed = null;
            if (root.TryGetProperty("context_window", out var ctxProp) && 
                ctxProp.TryGetProperty("used_percentage", out var usedVal) && 
                usedVal.ValueKind == JsonValueKind.Number)
            {
                contextUsed = usedVal.GetDouble();
            }

            long? inputTokens = null;
            if (root.TryGetProperty("total_input_tokens", out var inTok) && inTok.ValueKind == JsonValueKind.Number)
                inputTokens = inTok.GetInt64();

            long? outputTokens = null;
            if (root.TryGetProperty("total_output_tokens", out var outTok) && outTok.ValueKind == JsonValueKind.Number)
                outputTokens = outTok.GetInt64();
            
            // Also try capturing current usage breakdown if available
            long? cacheCreation = null;
            long? cacheRead = null;
            if (root.TryGetProperty("current_usage", out var usageProp))
            {
                if (usageProp.TryGetProperty("cache_creation_input_tokens", out var cc)) cacheCreation = cc.GetInt64();
                if (usageProp.TryGetProperty("cache_read_input_tokens", out var cr)) cacheRead = cr.GetInt64();
            }

            return new StatuslineData(
                ModelName: model,
                CostUsd: cost,
                ContextWindowUsedPercent: contextUsed,
                TotalInputTokens: inputTokens,
                TotalOutputTokens: outputTokens,
                CacheCreationTokens: cacheCreation,
                CacheReadTokens: cacheRead,
                CapturedAt: lastWrite
            );
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"StatuslineReader: Failed to read data: {ex.Message}");
            return null;
        }
    }
}
