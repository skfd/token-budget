using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using TokenBudget.Core;

namespace TokenBudget.Providers.Copilot;

/// <summary>
/// Calls the GitHub billing API to retrieve Copilot premium request usage.
/// Caches the result for 30 seconds to avoid excessive API calls.
/// </summary>
public sealed class CopilotUsageClient
{
    private const int ProQuotaLimit = 300;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    private static readonly IReadOnlyDictionary<string, string> GitHubHeaders = new Dictionary<string, string>
    {
        ["User-Agent"] = "TokenBudget/1.0",
        ["Accept"] = "application/json"
    };

    private readonly HttpGateway _gateway;
    private string? _cachedUsername;
    private OAuthUsageData? _cached;
    private DateTimeOffset _cachedAt = DateTimeOffset.MinValue;

    public CopilotUsageClient(HttpGateway gateway)
    {
        _gateway = gateway;
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
            var response = await _gateway.SendAsync(ApiEndpoint.GitHubCopilotUsage, token, ct,
                urlArg: _cachedUsername, extraHeaders: GitHubHeaders);
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
            double totalUsed = 0;
            foreach (var item in apiResponse.UsageItems)
                totalUsed += item.GrossQuantity;

            LastTotalUsed = (long)Math.Round(totalUsed);

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
            var response = await _gateway.SendAsync(ApiEndpoint.GitHubUser, token, ct, extraHeaders: GitHubHeaders);
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

    #region JSON deserialization models

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private sealed class ApiBillingResponse
    {
        public ApiBillingItem[]? UsageItems { get; set; }
    }

    private sealed class ApiBillingItem
    {
        public string Date { get; set; } = "";
        public double GrossQuantity { get; set; }
        public string Sku { get; set; } = "";
    }

    #endregion
}
