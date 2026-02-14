using System;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using LlmTokenWidget.Core;

namespace LlmTokenWidget.Providers.Zai;

/// <summary>
/// Calls the Z.ai quota endpoint to retrieve rate-limit data.
/// Caches the result for 30 seconds to avoid excessive API calls.
/// </summary>
public sealed class ZaiQuotaClient : IDisposable
{
    private const string QuotaUrl = "https://api.z.ai/api/monitor/usage/quota/limit";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    private readonly HttpClient _http;
    private OAuthUsageData? _cached;
    private DateTimeOffset _cachedAt = DateTimeOffset.MinValue;
    private bool _disposed;

    public ZaiQuotaClient()
    {
        _http = new HttpClient();
    }

    /// <summary>
    /// Fetches the current quota data from the API (or returns cached data if fresh).
    /// Returns null on any failure.
    /// </summary>
    public async Task<OAuthUsageData?> FetchAsync(CancellationToken ct = default)
    {
        if (_cached != null && DateTimeOffset.UtcNow - _cachedAt < CacheDuration)
            return _cached;

        try
        {
            var apiKey = CredentialReader.GetApiKey();
            if (string.IsNullOrEmpty(apiKey))
            {
                System.Diagnostics.Debug.WriteLine("ZaiQuotaClient: No API key available");
                return null;
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, QuotaUrl);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

            var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"ZaiQuotaClient: API returned {(int)response.StatusCode} {response.StatusCode}");
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            var apiResponse = JsonSerializer.Deserialize<ApiQuotaResponse>(json, JsonOptions);

            if (apiResponse?.Data?.Limits == null)
                return null;

            // API returns two TOKENS_LIMIT entries differentiated by unit:
            //   unit=3 (hours), number=5 → 5-hour rolling window
            //   unit=6 (weeks), number=1 → weekly quota
            RateLimitInfo? fiveHour = null;
            RateLimitInfo? weekly = null;

            foreach (var limit in apiResponse.Data.Limits)
            {
                if (limit.Type != "TOKENS_LIMIT")
                    continue;

                DateTimeOffset? resetsAt = null;
                if (limit.NextResetTime > 0)
                    resetsAt = DateTimeOffset.FromUnixTimeMilliseconds(limit.NextResetTime);

                var info = new RateLimitInfo(limit.Percentage, resetsAt);

                if (limit.Unit == 3) // hours
                    fiveHour = info;
                else if (limit.Unit == 6) // weeks
                    weekly = info;
            }

            _cached = new OAuthUsageData(
                FiveHour: fiveHour,
                SevenDay: weekly,
                SevenDayOAuthApps: null,
                SevenDayOpus: null,
                SevenDaySonnet: null,
                ExtraUsage: null,
                FetchedAt: DateTimeOffset.UtcNow);

            _cachedAt = DateTimeOffset.UtcNow;
            return _cached;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine($"ZaiQuotaClient: {ex.Message}");
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _http.Dispose();
    }

    #region JSON deserialization models

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed class ApiQuotaResponse
    {
        [JsonPropertyName("code")]
        public int Code { get; set; }

        [JsonPropertyName("data")]
        public ApiQuotaData? Data { get; set; }

        [JsonPropertyName("success")]
        public bool Success { get; set; }
    }

    private sealed class ApiQuotaData
    {
        [JsonPropertyName("limits")]
        public ApiLimitEntry[]? Limits { get; set; }
    }

    private sealed class ApiLimitEntry
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "";

        [JsonPropertyName("unit")]
        public int Unit { get; set; }

        [JsonPropertyName("number")]
        public int Number { get; set; }

        [JsonPropertyName("percentage")]
        public double Percentage { get; set; }

        [JsonPropertyName("nextResetTime")]
        public long NextResetTime { get; set; }
    }

    #endregion
}
