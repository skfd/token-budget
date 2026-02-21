namespace TokenBudget.Core;

/// <summary>
/// Breakdown of token counts by type.
/// </summary>
public record TokenBreakdown(
    long InputTokens,
    long OutputTokens,
    long CacheCreationTokens,
    long CacheReadTokens)
{
    /// <summary>Sum of all token types.</summary>
    public long Total => InputTokens + OutputTokens + CacheCreationTokens + CacheReadTokens;
}

/// <summary>
/// Rate limit data from a single window (5-hour, 7-day, etc.).
/// </summary>
public record RateLimitInfo(
    double Utilization,
    DateTimeOffset? ResetsAt);

/// <summary>
/// Extra/overflow usage info (for Max plans with overage billing).
/// </summary>
public record ExtraUsageInfo(
    bool IsEnabled,
    double MonthlyLimit,
    double UsedCredits,
    double Utilization);

/// <summary>
/// Full rate-limit snapshot from the OAuth usage API.
/// </summary>
public record OAuthUsageData(
    RateLimitInfo? FiveHour,
    RateLimitInfo? SevenDay,
    RateLimitInfo? SevenDayOAuthApps,
    RateLimitInfo? SevenDayOpus,
    RateLimitInfo? SevenDaySonnet,
    ExtraUsageInfo? ExtraUsage,
    DateTimeOffset FetchedAt,
    RateLimitInfo? Monthly = null);

/// <summary>
/// Aggregated token usage snapshot from a provider.
/// </summary>
public record UsageSnapshot(
    TokenBreakdown TotalTokens,
    int SessionCount,
    int MessageCount,
    DateTimeOffset? EarliestMessage,
    DateTimeOffset? LatestMessage,
    DateTimeOffset FetchedAt,
    StatuslineData? LiveStatus,
    OAuthUsageData? OAuthUsage);

/// <summary>
/// Whether a provider has data available.
/// </summary>
public record ProviderAvailability(
    bool IsAvailable,
    string? Message);

/// <summary>
/// Data captured from Claude Code's statusline stream.
/// </summary>
public record StatuslineData(
    string? ModelName,
    double? CostUsd,
    double? ContextWindowUsedPercent,
    long? TotalInputTokens,
    long? TotalOutputTokens,
    long? CacheCreationTokens,
    long? CacheReadTokens,
    DateTimeOffset CapturedAt);

