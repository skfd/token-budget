using LlmTokenWidget.Core;

namespace LlmTokenWidget.Providers.ClaudeCode;

/// <summary>
/// Estimates cooldown status based on a rolling 5-hour token window.
/// </summary>
public sealed class CooldownEstimator
{
    private readonly long _tokenLimit;
    private readonly TimeSpan _windowDuration;

    public CooldownEstimator(long tokenLimit, TimeSpan? windowDuration = null)
    {
        _tokenLimit = tokenLimit;
        _windowDuration = windowDuration ?? PlanLimits.WindowDuration;
    }

    /// <summary>
    /// Calculate cooldown from a list of token entries.
    /// </summary>
    /// <param name="entries">All token entries (will be filtered to the window).</param>
    /// <param name="now">Current time (for testability).</param>
    public CooldownEstimate Estimate(IReadOnlyList<TokenEntry> entries, DateTimeOffset? now = null)
    {
        var currentTime = now ?? DateTimeOffset.UtcNow;
        var windowStart = currentTime - _windowDuration;

        // Filter to entries within the rolling window
        var windowEntries = entries
            .Where(e => e.Timestamp >= windowStart && e.Timestamp <= currentTime)
            .OrderBy(e => e.Timestamp)
            .ToList();

        // Sum tokens in window
        long tokensInWindow = windowEntries.Sum(e => e.Tokens.Total);

        // Calculate percentage
        double percentUsed = _tokenLimit > 0
            ? (double)tokensInWindow / _tokenLimit
            : 0.0;

        // Determine status
        var status = percentUsed switch
        {
            >= 0.90 => CooldownStatus.Red,
            >= 0.70 => CooldownStatus.Yellow,
            _ => CooldownStatus.Green
        };

        // Calculate time until reset (when enough tokens expire from the window)
        TimeSpan? timeUntilReset = null;
        if (tokensInWindow >= _tokenLimit && windowEntries.Count > 0)
        {
            timeUntilReset = CalculateTimeUntilReset(windowEntries, tokensInWindow, currentTime);
        }

        return new CooldownEstimate(
            TokensInWindow: tokensInWindow,
            TokenLimit: _tokenLimit,
            PercentUsed: percentUsed,
            Status: status,
            TimeUntilReset: timeUntilReset,
            WindowDuration: _windowDuration);
    }

    /// <summary>
    /// Find when enough of the earliest entries will slide out of the window
    /// to bring us back under the limit.
    /// </summary>
    private TimeSpan? CalculateTimeUntilReset(
        List<TokenEntry> windowEntries, long tokensInWindow, DateTimeOffset now)
    {
        long tokensToFree = tokensInWindow - _tokenLimit;
        long freed = 0;

        foreach (var entry in windowEntries)
        {
            freed += entry.Tokens.Total;
            if (freed >= tokensToFree)
            {
                // This entry needs to slide out of the window.
                // It will expire at entry.Timestamp + windowDuration.
                var expiresAt = entry.Timestamp + _windowDuration;
                var remaining = expiresAt - now;
                return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
            }
        }

        // Shouldn't reach here, but if it does, all entries need to expire
        if (windowEntries.Count > 0)
        {
            var lastExpiry = windowEntries.Last().Timestamp + _windowDuration;
            var remaining = lastExpiry - now;
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }

        return null;
    }
}
