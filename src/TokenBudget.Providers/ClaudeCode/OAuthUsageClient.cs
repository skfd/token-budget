using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using LlmTokenWidget.Core;

namespace LlmTokenWidget.Providers.ClaudeCode;

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
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

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
        // Return memory cache if still fresh
        if (_cached != null && DateTimeOffset.UtcNow - _cachedAt < CacheDuration)
            return _cached;

        try
        {
            var token = OAuthCredentialReader.ReadAccessToken();
            if (string.IsNullOrEmpty(token))
            {
                System.Diagnostics.Debug.WriteLine("OAuthUsageClient: No access token available");
                return SimulateResets(_cached);
            }

            var response = await _gateway.SendAsync(ApiEndpoint.AnthropicOAuthUsage, token, ct, extraHeaders: ExtraHeaders);
            if (!response.IsSuccessStatusCode)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"OAuthUsageClient: API returned {(int)response.StatusCode} {response.StatusCode}");
                return SimulateResets(_cached);
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            var apiResponse = JsonSerializer.Deserialize<ApiUsageResponse>(json, JsonOptions);

            if (apiResponse == null)
                return SimulateResets(_cached);

            _cached = new OAuthUsageData(
                FiveHour: MapLimit(apiResponse.FiveHour),
                SevenDay: MapLimit(apiResponse.SevenDay),
                SevenDayOAuthApps: MapLimit(apiResponse.SevenDayOAuthApps),
                SevenDayOpus: MapLimit(apiResponse.SevenDayOpus),
                SevenDaySonnet: MapLimit(apiResponse.SevenDaySonnet),
                ExtraUsage: MapExtra(apiResponse.ExtraUsage),
                FetchedAt: DateTimeOffset.UtcNow);

            _cachedAt = DateTimeOffset.UtcNow;
            SaveDiskCache(_cached);
            return _cached;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine($"OAuthUsageClient: {ex.Message}");
            return SimulateResets(_cached);
        }
    }

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
