using System;
using System.Threading;
using System.Threading.Tasks;
using LlmTokenWidget.Core;

namespace LlmTokenWidget.Providers.Qwen;

/// <summary>
/// Provides placeholder usage data for Qwen.
/// </summary>
public sealed class UsageClient
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    private readonly HttpGateway _gateway;
    private OAuthUsageData? _cached;
    private DateTimeOffset _cachedAt = DateTimeOffset.MinValue;

    public UsageClient(HttpGateway gateway)
    {
        _gateway = gateway;
        
        // Initialize with placeholder data
        _cached = GeneratePlaceholderData();
        _cachedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Returns placeholder usage data for Qwen.
    /// </summary>
    public async Task<OAuthUsageData?> FetchAsync(CancellationToken ct = default)
    {
        // Return memory cache if still fresh
        if (_cached != null && DateTimeOffset.UtcNow - _cachedAt < CacheDuration)
            return _cached;

        // Update with new placeholder data periodically
        _cached = GeneratePlaceholderData();
        _cachedAt = DateTimeOffset.UtcNow;
        return _cached;
    }

    private static OAuthUsageData GeneratePlaceholderData()
    {
        var now = DateTimeOffset.UtcNow;
        var nextReset = now.AddHours(5); // Reset in 5 hours
        
        return new OAuthUsageData(
            FiveHour: new RateLimitInfo(
                Utilization: 65.0, // 65% utilized
                ResetsAt: nextReset),
            SevenDay: new RateLimitInfo(
                Utilization: 32.0, // 32% utilized
                ResetsAt: now.AddDays(7)), // Reset in 7 days
            SevenDayOAuthApps: new RateLimitInfo(
                Utilization: 40.0, 
                ResetsAt: now.AddDays(7)),
            SevenDayOpus: new RateLimitInfo(
                Utilization: 25.0,
                ResetsAt: now.AddDays(7)),
            SevenDaySonnet: new RateLimitInfo(
                Utilization: 55.0,
                ResetsAt: now.AddDays(7)),
            ExtraUsage: new ExtraUsageInfo(
                IsEnabled: false,
                MonthlyLimit: 0,
                UsedCredits: 0,
                Utilization: 0),
            FetchedAt: DateTimeOffset.UtcNow);
    }
}