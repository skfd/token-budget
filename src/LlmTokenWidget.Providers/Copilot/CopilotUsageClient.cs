using System;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using LlmTokenWidget.Core;

namespace LlmTokenWidget.Providers.Copilot;

/// <summary>
/// Calls the GitHub billing API to retrieve Copilot premium request usage.
/// Caches the result for 30 seconds to avoid excessive API calls.
/// </summary>
public sealed class CopilotUsageClient : IDisposable
{
    private const string UserUrl = "https://api.github.com/user";
    private const string UsageUrlTemplate = "https://api.github.com/users/{0}/settings/billing/premium_request/usage";
    private const int ProQuotaLimit = 300;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    private readonly HttpClient _http;
    private string? _cachedUsername;
    private OAuthUsageData? _cached;
    private DateTimeOffset _cachedAt = DateTimeOffset.MinValue;
    private bool _disposed;

    public CopilotUsageClient()
    {
        _http = new HttpClient();
        _http.DefaultRequestHeaders.Add("User-Agent", "LlmTokenWidget/1.0");
        _http.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    public async Task<OAuthUsageData?> FetchAsync(CancellationToken ct = default)
    {
        if (_cached != null && DateTimeOffset.UtcNow - _cachedAt < CacheDuration)
            return _cached;

        try
        {
            var token = CopilotCredentialReader.ReadToken();
            if (string.IsNullOrEmpty(token))
            {
                System.Diagnostics.Debug.WriteLine("CopilotUsageClient: No GitHub token available");
                return null;
            }

            // Fetch username (cached indefinitely per session)
            if (_cachedUsername == null)
            {
                _cachedUsername = await FetchUsernameAsync(token, ct);
                if (_cachedUsername == null)
                    return null;
            }

            // Fetch premium request usage
            var url = string.Format(UsageUrlTemplate, _cachedUsername);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"CopilotUsageClient: API returned {(int)response.StatusCode} {response.StatusCode}");
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            var apiResponse = JsonSerializer.Deserialize<ApiBillingResponse>(json, JsonOptions);

            if (apiResponse?.UsageItems == null)
                return null;

            // Sum all premium requests across models/SKUs
            long totalUsed = 0;
            foreach (var item in apiResponse.UsageItems)
                totalUsed += item.GrossQuantity;

            LastTotalUsed = totalUsed;

            // Calculate utilization against Pro quota (300/month)
            var utilization = ProQuotaLimit > 0 ? (double)totalUsed / ProQuotaLimit * 100 : 0;

            // Reset at 1st of next month UTC
            var now = DateTimeOffset.UtcNow;
            var nextReset = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(1);

            _cached = new OAuthUsageData(
                FiveHour: new RateLimitInfo(utilization, nextReset),
                SevenDay: null,
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
            System.Diagnostics.Debug.WriteLine($"CopilotUsageClient: {ex.Message}");
            return null;
        }
    }

    /// <summary>Total premium requests used this billing cycle.</summary>
    public long LastTotalUsed { get; private set; }

    private async Task<string?> FetchUsernameAsync(string token, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, UserUrl);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"CopilotUsageClient: /user returned {(int)response.StatusCode}");
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("login", out var login))
                return login.GetString();

            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine($"CopilotUsageClient: username fetch failed: {ex.Message}");
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
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private sealed class ApiBillingResponse
    {
        public ApiBillingItem[]? UsageItems { get; set; }
    }

    private sealed class ApiBillingItem
    {
        public string Date { get; set; } = "";
        public long GrossQuantity { get; set; }
        public string Sku { get; set; } = "";
    }

    #endregion
}
