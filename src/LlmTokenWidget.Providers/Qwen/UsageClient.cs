using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LlmTokenWidget.Core;

namespace LlmTokenWidget.Providers.Qwen;

/// <summary>
/// Estimates Qwen Coding Plan Lite rate limits by counting requests in rolling windows.
/// Limits: 1,200 requests / 5 hours, 9,000 / week (Monday reset), 18,000 / month.
/// </summary>
public sealed class UsageClient
{
    // Coding Plan Lite limits
    private const int FiveHourLimit = 1200;
    private const int WeeklyLimit = 9000;

    public Task<OAuthUsageData?> FetchAsync(List<DateTimeOffset> timestamps, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        // 5-hour rolling window
        var fiveHourAgo = now.AddHours(-5);
        int fiveHourCount = CountAfter(timestamps, fiveHourAgo);
        double fiveHourUtil = Math.Min(100.0, (double)fiveHourCount / FiveHourLimit * 100.0);
        var fiveHourReset = now.AddHours(5); // approximate: oldest request exits window

        // Weekly window: resets Monday 00:00 UTC+8 (Beijing time)
        var weekStart = GetLastMondayBeijing(now);
        int weeklyCount = CountAfter(timestamps, weekStart);
        double weeklyUtil = Math.Min(100.0, (double)weeklyCount / WeeklyLimit * 100.0);
        var nextMonday = weekStart.AddDays(7);

        var data = new OAuthUsageData(
            FiveHour: new RateLimitInfo(fiveHourUtil, fiveHourReset),
            SevenDay: new RateLimitInfo(weeklyUtil, nextMonday),
            SevenDayOAuthApps: null,
            SevenDayOpus: null,
            SevenDaySonnet: null,
            ExtraUsage: null,
            FetchedAt: now);

        return Task.FromResult<OAuthUsageData?>(data);
    }

    private static int CountAfter(List<DateTimeOffset> timestamps, DateTimeOffset cutoff)
    {
        int count = 0;
        foreach (var ts in timestamps)
        {
            if (ts >= cutoff) count++;
        }
        return count;
    }

    /// <summary>
    /// Returns the most recent Monday 00:00:00 in UTC+8 (Beijing time), converted to UTC.
    /// </summary>
    private static DateTimeOffset GetLastMondayBeijing(DateTimeOffset now)
    {
        var beijing = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(now, "China Standard Time");
        int daysSinceMonday = ((int)beijing.DayOfWeek - 1 + 7) % 7;
        var monday = beijing.Date.AddDays(-daysSinceMonday);
        return new DateTimeOffset(monday, TimeSpan.FromHours(8));
    }
}
