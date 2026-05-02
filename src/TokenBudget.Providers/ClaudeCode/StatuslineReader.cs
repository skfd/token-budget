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

            // Self-healing field resolution: try multiple paths in priority order.
            // Claude Code v2+ moved token totals under context_window; fall back to
            // root-level fields for older schema versions.
            var inputTokens  = ResolveNumber(root, "context_window.total_input_tokens",  "total_input_tokens");
            var outputTokens = ResolveNumber(root, "context_window.total_output_tokens", "total_output_tokens");

            // current_usage moved inside context_window in v2+
            long? cacheCreation = null;
            long? cacheRead = null;
            var usageProp = ResolveElement(root, "context_window.current_usage", "current_usage");
            if (usageProp is { } u)
            {
                if (u.TryGetProperty("cache_creation_input_tokens", out var cc)) cacheCreation = cc.GetInt64();
                if (u.TryGetProperty("cache_read_input_tokens",     out var cr)) cacheRead     = cr.GetInt64();
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

    /// <summary>
    /// Reads rate-limit data from the same widget-data.json file. Claude Code v2.1+
    /// embeds five_hour and seven_day usage directly, so we can avoid the OAuth API.
    /// Returns null if the file is missing/stale or the rate_limits block is absent.
    /// </summary>
    public OAuthUsageData? ReadRateLimits()
    {
        if (!File.Exists(_statusFilePath))
            return null;

        var lastWrite = File.GetLastWriteTimeUtc(_statusFilePath);
        if (DateTime.UtcNow - lastWrite > TimeSpan.FromMinutes(5))
            return null;

        try
        {
            using var stream = new FileStream(_statusFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (stream.Length == 0) return null;

            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;

            if (!root.TryGetProperty("rate_limits", out var rl) || rl.ValueKind != JsonValueKind.Object)
                return null;

            var fiveHour = ParseLimit(rl, "five_hour");
            var sevenDay = ParseLimit(rl, "seven_day");

            if (fiveHour == null && sevenDay == null)
                return null;

            return new OAuthUsageData(
                FiveHour: fiveHour,
                SevenDay: sevenDay,
                SevenDayOAuthApps: null,
                SevenDayOpus: null,
                SevenDaySonnet: null,
                ExtraUsage: null,
                FetchedAt: DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"StatuslineReader: Failed to read rate_limits: {ex.Message}");
            return null;
        }
    }

    private static RateLimitInfo? ParseLimit(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var entry) || entry.ValueKind != JsonValueKind.Object)
            return null;

        double pct = 0;
        if (entry.TryGetProperty("used_percentage", out var p) && p.ValueKind == JsonValueKind.Number)
            pct = p.GetDouble();

        DateTimeOffset? resets = null;
        if (entry.TryGetProperty("resets_at", out var r) && r.ValueKind == JsonValueKind.Number)
            resets = DateTimeOffset.FromUnixTimeSeconds(r.GetInt64());

        return new RateLimitInfo(pct, resets);
    }

    /// <summary>
    /// Tries each dot-separated path in order, returning the first numeric value found.
    /// </summary>
    private static long? ResolveNumber(JsonElement root, params string[] paths)
    {
        foreach (var path in paths)
        {
            var el = Navigate(root, path);
            if (el is { ValueKind: JsonValueKind.Number } v)
            {
                if (path != paths[0])
                    System.Diagnostics.Debug.WriteLine($"StatuslineReader: schema healed — '{paths[0]}' not found, used '{path}'");
                return v.GetInt64();
            }
        }
        return null;
    }

    /// <summary>
    /// Tries each dot-separated path in order, returning the first element found.
    /// </summary>
    private static JsonElement? ResolveElement(JsonElement root, params string[] paths)
    {
        foreach (var path in paths)
        {
            var el = Navigate(root, path);
            if (el.HasValue)
            {
                if (path != paths[0])
                    System.Diagnostics.Debug.WriteLine($"StatuslineReader: schema healed — '{paths[0]}' not found, used '{path}'");
                return el;
            }
        }
        return null;
    }

    /// <summary>
    /// Navigates a dot-separated path (e.g. "context_window.total_input_tokens") from a root element.
    /// Returns null if any segment is missing.
    /// </summary>
    private static JsonElement? Navigate(JsonElement root, string path)
    {
        var current = root;
        foreach (var segment in path.Split('.'))
        {
            if (!current.TryGetProperty(segment, out current))
                return null;
        }
        return current;
    }
}
