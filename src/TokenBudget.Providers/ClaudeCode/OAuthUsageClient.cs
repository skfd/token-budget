using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using TokenBudget.Core;

namespace TokenBudget.Providers.ClaudeCode;

/// <summary>
/// Calls the Anthropic OAuth usage endpoint to retrieve rate-limit data.
/// Caches the result for 30 seconds in memory and persists to disk so the
/// widget never shows zeros after the first successful fetch.
///
/// Known limitation: On cold start (widget process just launched), the OAuth
/// token may have expired since the last Claude Code session. We cannot refresh
/// it ourselves (requires client_id from Claude Code). The widget will show
/// disk-cached data (with reset simulation) until Claude Code is opened and
/// refreshes the token, at which point live data resumes automatically.
/// </summary>
public sealed class OAuthUsageClient
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    private static readonly IReadOnlyDictionary<string, string> ExtraHeaders = new Dictionary<string, string>
    {
        ["anthropic-beta"] = "oauth-2025-04-20",
        ["User-Agent"] = "claude-code/2.0.37"
    };

    private static readonly string DiskCachePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".claude", "oauth-usage-cache.json");

    private readonly HttpGateway _gateway;
    private OAuthUsageData? _cached;
    private DateTimeOffset _cachedAt = DateTimeOffset.MinValue;

    public OAuthUsageClient(HttpGateway gateway)
    {
        _gateway = gateway;

        // Load last-known data from disk so we never start at zero
        _cached = LoadDiskCache();
        if (_cached != null)
            _cachedAt = _cached.FetchedAt;
    }

    /// <summary>
    /// Fetches the current usage data from the API (or returns cached data if fresh).
    /// Falls back to disk-cached data (with simulated resets) on failure.
    /// </summary>
    public async Task<OAuthUsageData?> FetchAsync(CancellationToken ct = default)
    {
        // Return memory cache if still fresh (applies to both successful and failed fetches,
        // so failures also back off for CacheDuration instead of retrying every 5 seconds)
        if (DateTimeOffset.UtcNow - _cachedAt < CacheDuration)
            return SimulateResets(_cached);

        try
        {
            var token = OAuthCredentialReader.ReadAccessToken();
            if (string.IsNullOrEmpty(token))
            {
                System.Diagnostics.Debug.WriteLine("OAuthUsageClient: No access token available");
                _cachedAt = DateTimeOffset.UtcNow;
                return SimulateResets(_cached);
            }

            var response = await _gateway.SendAsync(ApiEndpoint.AnthropicOAuthUsage, token, ct, extraHeaders: ExtraHeaders);
            if (!response.IsSuccessStatusCode)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"OAuthUsageClient: API returned {(int)response.StatusCode} {response.StatusCode}");
                _cachedAt = DateTimeOffset.UtcNow;
                return SimulateResets(_cached);
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            var parsed = Parse(json, DateTimeOffset.UtcNow);

            if (parsed == null)
            {
                _cachedAt = DateTimeOffset.UtcNow;
                return SimulateResets(_cached);
            }

            _cached = parsed;
            _cachedAt = DateTimeOffset.UtcNow;
            SaveDiskCache(_cached);
            return _cached;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine($"OAuthUsageClient: {ex.Message}");
            _cachedAt = DateTimeOffset.UtcNow;
            return SimulateResets(_cached);
        }
    }

    /// <summary>
    /// Parses an /api/oauth/usage response. The newer <c>limits[]</c> and
    /// <c>spend</c> blocks are preferred; the legacy <c>five_hour</c>/<c>seven_day</c>
    /// and <c>extra_usage</c> fields are the fallback. Codenamed keys are ignored.
    /// </summary>
    public static OAuthUsageData? Parse(string json, DateTimeOffset fetchedAt)
    {
        var api = JsonSerializer.Deserialize<ApiUsageResponse>(json, JsonOptions);
        if (api == null) return null;

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        RateLimitInfo? session = null, weekly = null;
        var scoped = new List<ScopedLimitInfo>();
        if (root.TryGetProperty("limits", out var limits) && limits.ValueKind == JsonValueKind.Array)
        {
            foreach (var l in limits.EnumerateArray())
            {
                var kind = Str(l, "kind");
                if (!l.TryGetProperty("percent", out var pct) || pct.ValueKind != JsonValueKind.Number) continue;
                var info = new RateLimitInfo(pct.GetDouble(), Date(l, "resets_at"));
                switch (kind)
                {
                    case "session": session = info; break;
                    case "weekly_all": weekly = info; break;
                    case "weekly_scoped":
                        scoped.Add(new ScopedLimitInfo(ScopeLabel(l) ?? "Scoped", info.Utilization, info.ResetsAt));
                        break;
                }
            }
        }

        return new OAuthUsageData(
            FiveHour: session ?? MapLimit(api.FiveHour),
            SevenDay: weekly ?? MapLimit(api.SevenDay),
            SevenDayOAuthApps: MapLimit(api.SevenDayOAuthApps),
            SevenDayOpus: MapLimit(api.SevenDayOpus),
            SevenDaySonnet: MapLimit(api.SevenDaySonnet),
            ExtraUsage: MapExtra(api.ExtraUsage),
            FetchedAt: fetchedAt,
            ScopedLimits: scoped.Count > 0 ? scoped : null,
            Credits: ParseCredits(root),
            WeeklyBreakdown: ParseBreakdown(root));
    }

    private static CreditInfo? ParseCredits(JsonElement root)
    {
        if (root.TryGetProperty("spend", out var spend) && spend.ValueKind == JsonValueKind.Object)
        {
            var used = Money(spend, "used");
            var limit = Money(spend, "limit");
            if (limit != null)
            {
                var enabled = spend.TryGetProperty("enabled", out var e) && e.ValueKind == JsonValueKind.True;
                return new CreditInfo(enabled, used?.Amount ?? 0, limit.Value.Amount, limit.Value.Currency,
                    Str(spend, "disabled_reason"));
            }
        }

        // Legacy: extra_usage amounts are minor units with an optional currency.
        if (root.TryGetProperty("extra_usage", out var x) && x.ValueKind == JsonValueKind.Object &&
            x.TryGetProperty("monthly_limit", out var ml) && ml.ValueKind == JsonValueKind.Number)
        {
            var places = x.TryGetProperty("decimal_places", out var dp) && dp.ValueKind == JsonValueKind.Number ? dp.GetInt32() : 2;
            var scale = Math.Pow(10, places);
            var used = x.TryGetProperty("used_credits", out var uc) && uc.ValueKind == JsonValueKind.Number ? uc.GetDouble() : 0;
            var enabled = x.TryGetProperty("is_enabled", out var ie) && ie.ValueKind == JsonValueKind.True;
            return new CreditInfo(enabled, used / scale, ml.GetDouble() / scale, Str(x, "currency") ?? "USD",
                Str(x, "disabled_reason"));
        }
        return null;
    }

    private static (double Amount, string Currency)? Money(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var m) || m.ValueKind != JsonValueKind.Object) return null;
        if (!m.TryGetProperty("amount_minor", out var a) || a.ValueKind != JsonValueKind.Number) return null;
        var exp = m.TryGetProperty("exponent", out var e) && e.ValueKind == JsonValueKind.Number ? e.GetInt32() : 2;
        return (a.GetDouble() / Math.Pow(10, exp), Str(m, "currency") ?? "USD");
    }

    private static IReadOnlyList<UsageShare>? ParseBreakdown(JsonElement root)
    {
        if (!root.TryGetProperty("seven_day_breakdown", out var b) || b.ValueKind != JsonValueKind.Object ||
            !b.TryGetProperty("rows", out var rows) || rows.ValueKind != JsonValueKind.Array)
            return null;
        var list = new List<UsageShare>();
        foreach (var r in rows.EnumerateArray())
        {
            if (!r.TryGetProperty("percent", out var p) || p.ValueKind != JsonValueKind.Number) continue;
            list.Add(new UsageShare(Str(r, "display_name") ?? Str(r, "key") ?? "?", p.GetDouble()));
        }
        return list.Count > 0 ? list : null;
    }

    private static string? ScopeLabel(JsonElement limit)
    {
        if (!limit.TryGetProperty("scope", out var scope) || scope.ValueKind != JsonValueKind.Object) return null;
        foreach (var key in new[] { "model", "surface" })
        {
            if (!scope.TryGetProperty(key, out var v)) continue;
            if (v.ValueKind == JsonValueKind.String) return v.GetString();
            if (v.ValueKind == JsonValueKind.Object) return Str(v, "display_name") ?? Str(v, "id");
        }
        return null;
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static DateTimeOffset? Date(JsonElement e, string name) =>
        Str(e, name) is { } s && DateTimeOffset.TryParse(s, out var d) ? d : null;

    /// <summary>
    /// If a cached limit's reset time has passed, zero out its utilization
    /// so the widget doesn't show stale high percentages after a reset.
    /// </summary>
    private static OAuthUsageData? SimulateResets(OAuthUsageData? data)
    {
        if (data == null) return null;
        var now = DateTimeOffset.UtcNow;
        return data with
        {
            FiveHour = ResetIfExpired(data.FiveHour, now),
            SevenDay = ResetIfExpired(data.SevenDay, now),
            SevenDayOAuthApps = ResetIfExpired(data.SevenDayOAuthApps, now),
            SevenDayOpus = ResetIfExpired(data.SevenDayOpus, now),
            SevenDaySonnet = ResetIfExpired(data.SevenDaySonnet, now),
            ScopedLimits = data.ScopedLimits?
                .Select(l => l.ResetsAt is { } r && now >= r ? l with { Utilization = 0, ResetsAt = null } : l)
                .ToList(),
        };
    }

    private static RateLimitInfo? ResetIfExpired(RateLimitInfo? info, DateTimeOffset now)
    {
        if (info?.ResetsAt == null) return info;
        if (now >= info.ResetsAt.Value)
            return new RateLimitInfo(0, null);
        return info;
    }

    private static OAuthUsageData? LoadDiskCache()
    {
        try
        {
            if (!File.Exists(DiskCachePath)) return null;
            var json = File.ReadAllText(DiskCachePath);
            return JsonSerializer.Deserialize<OAuthUsageData>(json, DiskCacheOptions);
        }
        catch
        {
            return null;
        }
    }

    private static void SaveDiskCache(OAuthUsageData data)
    {
        try
        {
            var json = JsonSerializer.Serialize(data, DiskCacheOptions);
            File.WriteAllText(DiskCachePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"OAuthUsageClient: Failed to save disk cache: {ex.Message}");
        }
    }

    private static RateLimitInfo? MapLimit(ApiLimitEntry? entry)
    {
        if (entry == null) return null;
        DateTimeOffset? resetsAt = null;
        if (!string.IsNullOrEmpty(entry.ResetsAt) &&
            DateTimeOffset.TryParse(entry.ResetsAt, out var parsed))
        {
            resetsAt = parsed;
        }
        return new RateLimitInfo(entry.Utilization, resetsAt);
    }

    private static ExtraUsageInfo? MapExtra(ApiExtraUsageEntry? entry)
    {
        if (entry == null) return null;
        return new ExtraUsageInfo(
            entry.IsEnabled,
            entry.MonthlyLimit,
            entry.UsedCredits,
            entry.Utilization);
    }

    #region JSON deserialization models

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private static readonly JsonSerializerOptions DiskCacheOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private sealed class ApiUsageResponse
    {
        public ApiLimitEntry? FiveHour { get; set; }
        public ApiLimitEntry? SevenDay { get; set; }
        public ApiLimitEntry? SevenDayOAuthApps { get; set; }
        public ApiLimitEntry? SevenDayOpus { get; set; }
        public ApiLimitEntry? SevenDaySonnet { get; set; }
        public ApiLimitEntry? SevenDayCowork { get; set; }
        public ApiExtraUsageEntry? ExtraUsage { get; set; }
    }

    private sealed class ApiLimitEntry
    {
        public double Utilization { get; set; }
        public string? ResetsAt { get; set; }
    }

    private sealed class ApiExtraUsageEntry
    {
        public bool IsEnabled { get; set; }
        public double MonthlyLimit { get; set; }
        public double UsedCredits { get; set; }
        public double Utilization { get; set; }
    }

    #endregion
}
