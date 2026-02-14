namespace LlmTokenWidget.Core;

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
/// A single token usage entry from one assistant message.
/// </summary>
public record TokenEntry(
    DateTimeOffset Timestamp,
    string Uuid,
    string Model,
    TokenBreakdown Tokens);

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
    StatuslineData? LiveStatus);

/// <summary>
/// Cooldown status for a rolling-window token budget.
/// </summary>
public record CooldownEstimate(
    /// <summary>Tokens consumed within the rolling window.</summary>
    long TokensInWindow,

    /// <summary>Maximum tokens allowed in the window.</summary>
    long TokenLimit,

    /// <summary>0.0–1.0+ percentage of budget consumed.</summary>
    double PercentUsed,

    /// <summary>Green/Yellow/Red status indicator.</summary>
    CooldownStatus Status,

    /// <summary>If over limit, estimated time until enough tokens expire from the window.</summary>
    TimeSpan? TimeUntilReset,

    /// <summary>Duration of the rolling window.</summary>
    TimeSpan WindowDuration);

/// <summary>
/// Traffic-light status for cooldown.
/// </summary>
public enum CooldownStatus
{
    /// <summary>Below 70% — safe to use freely.</summary>
    Green,

    /// <summary>70–90% — approaching limit.</summary>
    Yellow,

    /// <summary>Above 90% — near or at limit.</summary>
    Red
}

/// <summary>
/// Whether a provider has data available.
/// </summary>
public record ProviderAvailability(
    bool IsAvailable,
    string? Message);

/// <summary>
/// Known Claude subscription plan tiers with their token budgets.
/// </summary>
public static class PlanLimits
{
    /// <summary>Rolling window duration.</summary>
    public static readonly TimeSpan WindowDuration = TimeSpan.FromHours(5);

    /// <summary>Pro plan: ~45M tokens per 5h.</summary>
    public const long Pro = 45_000_000;

    /// <summary>Max5 plan: ~135M tokens per 5h.</summary>
    public const long Max5 = 135_000_000;

    /// <summary>Max20 plan: ~540M tokens per 5h.</summary>
    public const long Max20 = 540_000_000;
}

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

