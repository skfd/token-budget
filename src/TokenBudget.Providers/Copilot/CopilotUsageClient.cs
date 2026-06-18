using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TokenBudget.Core;

namespace TokenBudget.Providers.Copilot;

/// <summary>
/// Calls the GitHub Copilot quota endpoint to retrieve premium request usage.
/// Caches the result for 30 seconds to avoid excessive API calls.
/// </summary>
public sealed class CopilotUsageClient
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    private static readonly IReadOnlyDictionary<string, string> GitHubHeaders = new Dictionary<string, string>
    {
        ["User-Agent"] = "TokenBudget/1.0",
        ["Accept"] = "application/json"
    };

    private readonly HttpGateway _gateway;
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

            var response = await _gateway.SendAsync(ApiEndpoint.GitHubCopilotUsage, token, ct,
                extraHeaders: GitHubHeaders);
            if (!response.IsSuccessStatusCode)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"CopilotUsageClient: API returned {(int)response.StatusCode} {response.StatusCode}");
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            var apiResponse = JsonSerializer.Deserialize<ApiCopilotUser>(json, JsonOptions);

            var premium = apiResponse?.QuotaSnapshots?.PremiumInteractions;
            if (premium == null)
                return null;

            QuotaLimit = (long)Math.Round(premium.Entitlement);
            // "used" = entitlement minus what remains in the rolling monthly quota
            LastTotalUsed = (long)Math.Round(Math.Max(0, premium.Entitlement - premium.QuotaRemaining));

            // API reports remaining %; widget shows utilization (% used)
            var utilization = premium.Unlimited ? 0 : Math.Max(0, 100 - premium.PercentRemaining);

            var nextReset = apiResponse!.QuotaResetDateUtc ?? FirstOfNextMonthUtc();

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

    /// <summary>Premium requests used this billing cycle.</summary>
    public long LastTotalUsed { get; private set; }

    /// <summary>Monthly premium request entitlement for the current plan.</summary>
    public long QuotaLimit { get; private set; }

    private static DateTimeOffset FirstOfNextMonthUtc()
    {
        var now = DateTimeOffset.UtcNow;
        return new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(1);
    }

    #region JSON deserialization models

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    private sealed class ApiCopilotUser
    {
        public DateTimeOffset? QuotaResetDateUtc { get; set; }
        public ApiQuotaSnapshots? QuotaSnapshots { get; set; }
    }

    private sealed class ApiQuotaSnapshots
    {
        public ApiQuota? PremiumInteractions { get; set; }
    }

    private sealed class ApiQuota
    {
        public double Entitlement { get; set; }
        public double QuotaRemaining { get; set; }
        public double PercentRemaining { get; set; }
        public bool Unlimited { get; set; }
    }

    #endregion
}
