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

// TokenEntry removed (unused)

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

// Cooldown and Plan limits removed (unused)

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

