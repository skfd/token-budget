using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Windows.Widgets;
using Microsoft.Windows.Widgets.Providers;
using TokenBudget.Core;
using TokenBudget.Providers.ClaudeCode;
using TokenBudget.Providers.Copilot;
using TokenBudget.Providers.Qwen;
using TokenBudget.Providers.Zai;

namespace TokenBudget.App;

[ComVisible(true)]
[ComDefaultInterface(typeof(IWidgetProvider))]
[Guid("9F910C81-08A4-461F-93A6-96809C70A95D")] // CRITICAL: Must match Program.cs and manifest
public sealed class WidgetProvider : IWidgetProvider, IWidgetProvider2
{
    private const string ClaudeWidgetId = "Claude_Usage_Widget";
    private const string ZaiWidgetId = "Zai_Usage_Widget";
    private const string CopilotWidgetId = "Copilot_Usage_Widget";
    private const string QwenWidgetId = "Qwen_Usage_Widget";

    private readonly Dictionary<string, WidgetState> _activeWidgets = new();
    private readonly Dictionary<string, ILlmProvider> _providers = new();
    private Timer? _refreshTimer;
    private readonly object _lock = new();

    private sealed class WidgetState
    {
        public string DefinitionId { get; set; } = "";
        public WidgetSize Size { get; set; }
    }

    public WidgetProvider()
    {
        System.Diagnostics.Debug.WriteLine("WidgetProvider constructor called");

        var gateway = new TokenBudget.Core.HttpGateway();

        var claudeProvider = new ClaudeCodeLocalProvider(gateway);
        claudeProvider.DataChanged += OnProviderDataChanged;
        _providers[ClaudeWidgetId] = claudeProvider;

        var zaiProvider = new ZaiLocalProvider(gateway);
        zaiProvider.DataChanged += OnProviderDataChanged;
        _providers[ZaiWidgetId] = zaiProvider;

        _providers[CopilotWidgetId] = new CopilotProvider(gateway);

        var qwenProvider = new QwenLocalProvider(gateway);
        qwenProvider.DataChanged += OnProviderDataChanged;
        _providers[QwenWidgetId] = qwenProvider;
    }

    public void CreateWidget(WidgetContext widgetContext)
    {
        System.Diagnostics.Debug.WriteLine($"CreateWidget called for: {widgetContext.Id}");
        _activeWidgets[widgetContext.Id] = new WidgetState
        {
            DefinitionId = widgetContext.DefinitionId,
            Size = widgetContext.Size
        };

        EnsureTimerRunning();
        UpdateWidget(widgetContext.Id);
    }

    public void DeleteWidget(string widgetId, string customState)
    {
        _activeWidgets.Remove(widgetId);
        StopTimerIfNoWidgets();
    }

    public void OnActionInvoked(WidgetActionInvokedArgs actionInvokedArgs)
    {
        // Handle button clicks in Phase 3 (settings)
    }

    public void OnCustomizationRequested(WidgetCustomizationRequestedArgs customizationRequestedArgs)
    {
        // Show settings panel in Phase 3
    }

    public void OnWidgetContextChanged(WidgetContextChangedArgs contextChangedArgs)
    {
        if (_activeWidgets.TryGetValue(contextChangedArgs.WidgetContext.Id, out var state))
        {
            state.Size = contextChangedArgs.WidgetContext.Size;
        }
        UpdateWidget(contextChangedArgs.WidgetContext.Id);
    }

    public void Activate(WidgetContext widgetContext)
    {
        try
        {
            CreateWidget(widgetContext);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Activate failed: {ex.Message}");
        }
    }

    public void Deactivate(string widgetId)
    {
        try
        {
            DeleteWidget(widgetId, "");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Deactivate failed: {ex.Message}");
        }
    }

    private void OnProviderDataChanged(object? sender, EventArgs e)
    {
        RefreshAllWidgets();
    }

    private void EnsureTimerRunning()
    {
        lock (_lock)
        {
            if (_refreshTimer == null)
            {
                var minInterval = TimeSpan.FromSeconds(5);
                _refreshTimer = new Timer(_ => RefreshAllWidgets(),
                    null,
                    minInterval,
                    minInterval);
            }
        }
    }

    private void StopTimerIfNoWidgets()
    {
        lock (_lock)
        {
            if (_activeWidgets.Count == 0)
            {
                _refreshTimer?.Dispose();
                _refreshTimer = null;
            }
        }
    }

    private void RefreshAllWidgets()
    {
        foreach (var widgetId in _activeWidgets.Keys)
        {
            try
            {
                UpdateWidget(widgetId);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RefreshAllWidgets: {widgetId} failed: {ex.Message}");
            }
        }
    }

    private void UpdateWidget(string widgetId)
    {
        try
        {
            if (!_activeWidgets.TryGetValue(widgetId, out var state))
                return;

            if (!_providers.TryGetValue(state.DefinitionId, out var provider))
                return;

            var usageTask = provider.FetchUsageAsync(CancellationToken.None);
            var usage = usageTask.GetAwaiter().GetResult();

            var templateJson = GetTemplate(state.DefinitionId, state.Size);
            var dataJson = BuildDataJson(usage, state.DefinitionId, state.Size);

            var updateOptions = new WidgetUpdateRequestOptions(widgetId)
            {
                Template = templateJson,
                Data = dataJson
            };

            WidgetManager.GetDefault().UpdateWidget(updateOptions);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"UpdateWidget failed: {ex.Message}");
        }
    }

    private string BuildDataJson(UsageSnapshot usage, string definitionId, WidgetSize size)
    {
        return definitionId switch
        {
            ZaiWidgetId => BuildZaiDataJson(usage, size),
            CopilotWidgetId => BuildCopilotDataJson(usage, size),
            QwenWidgetId => BuildQwenDataJson(usage, size),
            _ => BuildClaudeDataJson(usage, size)
        };
    }

    private string BuildClaudeDataJson(UsageSnapshot usage, WidgetSize size)
    {
        var total = usage.TotalTokens;
        var oauth = usage.OAuthUsage;

        // Primary limit: 5-hour window
        var fiveHour = oauth?.FiveHour;
        var sevenDay = oauth?.SevenDay;
        var extra = oauth?.ExtraUsage;

        // Utilization percentage from 5h window
        var utilization = fiveHour?.Utilization ?? 0;
        var percentText = fiveHour != null ? $"{utilization:F0}%" : "—%";
        var percentValue = (int)Math.Round(utilization);
        var percentValueClamped = Math.Max(1, Math.Min(100, percentValue));
        if (fiveHour == null) percentValueClamped = 0;

        // Status visuals based on utilization
        var (barStyle, statusColor) = GetStatusVisuals(utilization, fiveHour != null);

        // Reset countdown
        var resetText = "";
        if (fiveHour?.ResetsAt != null)
        {
            var remaining = fiveHour.ResetsAt.Value - DateTimeOffset.Now;
            if (remaining.TotalSeconds > 0)
            {
                resetText = remaining.TotalHours >= 1
                    ? $"Resets in {(int)remaining.TotalHours} hr {remaining.Minutes} min"
                    : $"Resets in {remaining.Minutes} min";
            }
        }

        // 7-day data (split for template use)
        var sevenDayPercent = sevenDay != null ? $"{sevenDay.Utilization:F0}%" : "—%";
        var sevenDayValue = sevenDay != null ? (int)Math.Round(sevenDay.Utilization) : 0;
        var sevenDayValueClamped = sevenDay != null ? Math.Max(1, Math.Min(100, sevenDayValue)) : 0;
        var sevenDayReset = "";
        if (sevenDay?.ResetsAt != null)
        {
            var remaining7d = sevenDay.ResetsAt.Value - DateTimeOffset.Now;
            if (remaining7d.TotalSeconds > 0)
            {
                sevenDayReset = $"Resets {sevenDay.ResetsAt.Value.LocalDateTime:ddd h:mm tt}";
            }
        }
        var (sevenDayBarStyle, sevenDayStatusColor) = GetStatusVisuals(sevenDay?.Utilization ?? 0, sevenDay != null);

        // Extra usage info
        var extraText = "";
        if (extra is { IsEnabled: true })
        {
            extraText = $"Overage: ${extra.UsedCredits / 100.0:F0}/${extra.MonthlyLimit / 100.0:F0} ({extra.Utilization:F0}%)";
        }

        var planName = "Claude Code";

        // Extract live status data if available
        var live = usage.LiveStatus;
        var costText = live?.CostUsd.HasValue == true ? $"${live.CostUsd.Value:F2}" : "";
        var modelText = !string.IsNullOrEmpty(live?.ModelName) ? live.ModelName : "Claude Code";
        var contextText = live?.ContextWindowUsedPercent.HasValue == true ? $"Ctx: {live.ContextWindowUsedPercent.Value:F1}%" : "";

        // Remaining bar width for progress bar background
        var percentRemaining = Math.Max(1, 100 - percentValueClamped);
        var sevenDayRemaining = Math.Max(1, 100 - sevenDayValueClamped);

        return $$"""
        {
            "barStyle": "{{barStyle}}",
            "statusColor": "{{statusColor}}",
            "percentText": "{{percentText}}",
            "percentValue": {{percentValue}},
            "percentValueClamped": {{percentValueClamped}},
            "percentRemaining": {{percentRemaining}},
            "totalTokens": "{{FormatNumber(total.Total)}}",
            "inputTokens": "{{FormatNumber(total.InputTokens)}}",
            "outputTokens": "{{FormatNumber(total.OutputTokens)}}",
            "cacheCreation": "{{FormatNumber(total.CacheCreationTokens)}}",
            "cacheRead": "{{FormatNumber(total.CacheReadTokens)}}",
            "messageCount": "{{usage.MessageCount}}",
            "resetTime": "{{resetText}}",
            "sevenDayBarStyle": "{{sevenDayBarStyle}}",
            "sevenDayStatusColor": "{{sevenDayStatusColor}}",
            "sevenDayPercent": "{{sevenDayPercent}}",
            "sevenDayValue": {{sevenDayValue}},
            "sevenDayValueClamped": {{sevenDayValueClamped}},
            "sevenDayRemaining": {{sevenDayRemaining}},
            "sevenDayReset": "{{sevenDayReset}}",
            "extraUsage": "{{extraText}}",
            "planName": "{{planName}}",
            "cost": "{{costText}}",
            "model": "{{modelText}}",
            "context": "{{contextText}}",
            "size": "{{size}}"
        }
        """;
    }

    private string BuildQwenDataJson(UsageSnapshot usage, WidgetSize size)
    {
        var total = usage.TotalTokens;
        var oauth = usage.OAuthUsage;

        // Primary limit: 5-hour window
        var fiveHour = oauth?.FiveHour;
        var sevenDay = oauth?.SevenDay;

        // Utilization percentage from 5h window
        var utilization = fiveHour?.Utilization ?? 0;
        var percentText = fiveHour != null ? $"{utilization:F0}%" : "—%";
        var percentValue = (int)Math.Round(utilization);
        var percentValueClamped = Math.Max(1, Math.Min(100, percentValue));
        if (fiveHour == null) percentValueClamped = 0;

        // Status visuals based on utilization
        var (barStyle, statusColor) = GetStatusVisuals(utilization, fiveHour != null);

        // Reset countdown
        var resetText = "";
        if (fiveHour?.ResetsAt != null)
        {
            var remaining = fiveHour.ResetsAt.Value - DateTimeOffset.Now;
            if (remaining.TotalSeconds > 0)
            {
                resetText = remaining.TotalHours >= 1
                    ? $"Resets in {(int)remaining.TotalHours} hr {remaining.Minutes} min"
                    : $"Resets in {remaining.Minutes} min";
            }
        }

        // 7-day data (split for template use)
        var sevenDayPercent = sevenDay != null ? $"{sevenDay.Utilization:F0}%" : "—%";
        var sevenDayValue = sevenDay != null ? (int)Math.Round(sevenDay.Utilization) : 0;
        var sevenDayValueClamped = sevenDay != null ? Math.Max(1, Math.Min(100, sevenDayValue)) : 0;
        var sevenDayReset = "";
        if (sevenDay?.ResetsAt != null)
        {
            var remaining7d = sevenDay.ResetsAt.Value - DateTimeOffset.Now;
            if (remaining7d.TotalSeconds > 0)
            {
                sevenDayReset = $"Resets {sevenDay.ResetsAt.Value.LocalDateTime:ddd h:mm tt}";
            }
        }
        var (sevenDayBarStyle, sevenDayStatusColor) = GetStatusVisuals(sevenDay?.Utilization ?? 0, sevenDay != null);

        // Monthly quota
        var monthly = oauth?.Monthly;
        var monthlyPercent = monthly != null ? $"{monthly.Utilization:F0}%" : "—%";
        var monthlyValue = monthly != null ? (int)Math.Round(monthly.Utilization) : 0;
        var monthlyValueClamped = monthly != null ? Math.Max(1, Math.Min(100, monthlyValue)) : 0;
        var monthlyRemaining = Math.Max(1, 100 - monthlyValueClamped);
        var monthlyReset = "";
        if (monthly?.ResetsAt != null)
        {
            var remainingM = monthly.ResetsAt.Value - DateTimeOffset.Now;
            if (remainingM.TotalSeconds > 0)
            {
                monthlyReset = $"Resets {monthly.ResetsAt.Value.LocalDateTime:MMM d}";
            }
        }
        var (monthlyBarStyle, monthlyStatusColor) = GetStatusVisuals(monthly?.Utilization ?? 0, monthly != null);

        // No overage info for Qwen
        var extraText = "";

        var planName = "Qwen";

        // Extract live status data if available
        var live = usage.LiveStatus;
        var costText = live?.CostUsd.HasValue == true ? $"${live.CostUsd.Value:F2}" : "";
        var modelText = !string.IsNullOrEmpty(live?.ModelName) ? live.ModelName : "Qwen";
        var contextText = live?.ContextWindowUsedPercent.HasValue == true ? $"Ctx: {live.ContextWindowUsedPercent.Value:F1}%" : "";

        // Remaining bar width for progress bar background
        var percentRemaining = Math.Max(1, 100 - percentValueClamped);
        var sevenDayRemaining = Math.Max(1, 100 - sevenDayValueClamped);

        return $$"""
        {
            "barStyle": "{{barStyle}}",
            "statusColor": "{{statusColor}}",
            "percentText": "{{percentText}}",
            "percentValue": {{percentValue}},
            "percentValueClamped": {{percentValueClamped}},
            "percentRemaining": {{percentRemaining}},
            "totalTokens": "{{FormatNumber(total.Total)}}",
            "inputTokens": "{{FormatNumber(total.InputTokens)}}",
            "outputTokens": "{{FormatNumber(total.OutputTokens)}}",
            "cacheCreation": "{{FormatNumber(total.CacheCreationTokens)}}",
            "cacheRead": "{{FormatNumber(total.CacheReadTokens)}}",
            "messageCount": "{{usage.MessageCount}}",
            "resetTime": "{{resetText}}",
            "sevenDayBarStyle": "{{sevenDayBarStyle}}",
            "sevenDayStatusColor": "{{sevenDayStatusColor}}",
            "sevenDayPercent": "{{sevenDayPercent}}",
            "sevenDayValue": {{sevenDayValue}},
            "sevenDayValueClamped": {{sevenDayValueClamped}},
            "sevenDayRemaining": {{sevenDayRemaining}},
            "sevenDayReset": "{{sevenDayReset}}",
            "monthlyBarStyle": "{{monthlyBarStyle}}",
            "monthlyStatusColor": "{{monthlyStatusColor}}",
            "monthlyPercent": "{{monthlyPercent}}",
            "monthlyValue": {{monthlyValue}},
            "monthlyValueClamped": {{monthlyValueClamped}},
            "monthlyRemaining": {{monthlyRemaining}},
            "monthlyReset": "{{monthlyReset}}",
            "extraUsage": "{{extraText}}",
            "planName": "{{planName}}",
            "cost": "{{costText}}",
            "model": "{{modelText}}",
            "context": "{{contextText}}",
            "size": "{{size}}"
        }
        """;
    }

    private string BuildZaiDataJson(UsageSnapshot usage, WidgetSize size)
    {
        var total = usage.TotalTokens;
        var oauth = usage.OAuthUsage;
        var fiveHour = oauth?.FiveHour;
        var weekly = oauth?.SevenDay;

        var providerName = "Z.ai";

        // 5-hour utilization
        var utilization = fiveHour?.Utilization ?? 0;
        var percentText = fiveHour != null ? $"{utilization:F0}%" : "—%";
        var percentValue = (int)Math.Round(utilization);
        var percentValueClamped = Math.Max(1, Math.Min(100, percentValue));
        if (fiveHour == null) percentValueClamped = 0;

        var (barStyle, statusColor) = GetStatusVisuals(utilization, fiveHour != null);

        var resetText = "";
        if (fiveHour?.ResetsAt != null)
        {
            var remaining = fiveHour.ResetsAt.Value - DateTimeOffset.Now;
            if (remaining.TotalSeconds > 0)
            {
                resetText = remaining.TotalHours >= 1
                    ? $"Resets in {(int)remaining.TotalHours} hr {remaining.Minutes} min"
                    : $"Resets in {remaining.Minutes} min";
            }
        }

        var percentRemaining = Math.Max(1, 100 - percentValueClamped);

        // Weekly quota
        var weeklyPercent = weekly != null ? $"{weekly.Utilization:F0}%" : "—%";
        var weeklyValue = weekly != null ? (int)Math.Round(weekly.Utilization) : 0;
        var weeklyValueClamped = weekly != null ? Math.Max(1, Math.Min(100, weeklyValue)) : 0;
        var weeklyRemaining = Math.Max(1, 100 - weeklyValueClamped);
        var weeklyReset = "";
        if (weekly?.ResetsAt != null)
        {
            var remaining = weekly.ResetsAt.Value - DateTimeOffset.Now;
            if (remaining.TotalSeconds > 0)
            {
                weeklyReset = $"Resets {weekly.ResetsAt.Value.LocalDateTime:ddd MMM d, h:mm tt}";
            }
        }
        var (weeklyBarStyle, weeklyStatusColor) = GetStatusVisuals(weekly?.Utilization ?? 0, weekly != null);

        return $$"""
        {
            "providerName": "{{providerName}}",
            "barStyle": "{{barStyle}}",
            "statusColor": "{{statusColor}}",
            "percentText": "{{percentText}}",
            "percentValue": {{percentValue}},
            "percentValueClamped": {{percentValueClamped}},
            "percentRemaining": {{percentRemaining}},
            "resetTime": "{{resetText}}",
            "weeklyBarStyle": "{{weeklyBarStyle}}",
            "weeklyStatusColor": "{{weeklyStatusColor}}",
            "weeklyPercent": "{{weeklyPercent}}",
            "weeklyValue": {{weeklyValue}},
            "weeklyValueClamped": {{weeklyValueClamped}},
            "weeklyRemaining": {{weeklyRemaining}},
            "weeklyReset": "{{weeklyReset}}",
            "totalTokens": "{{FormatNumber(total.Total)}}",
            "inputTokens": "{{FormatNumber(total.InputTokens)}}",
            "outputTokens": "{{FormatNumber(total.OutputTokens)}}",
            "reasoningTokens": "{{FormatNumber(0)}}",
            "cacheWrite": "{{FormatNumber(total.CacheCreationTokens)}}",
            "cacheRead": "{{FormatNumber(total.CacheReadTokens)}}",
            "sessionCount": "{{usage.SessionCount}}",
            "messageCount": "{{usage.MessageCount}}",
            "earliestMessage": "{{usage.EarliestMessage?.LocalDateTime:MMM d, h:mm tt}}",
            "latestMessage": "{{usage.LatestMessage?.LocalDateTime:MMM d, h:mm tt}}",
            "size": "{{size}}"
        }
        """;
    }

    private string BuildCopilotDataJson(UsageSnapshot usage, WidgetSize size)
    {
        var monthly = usage.OAuthUsage?.FiveHour; // Monthly quota stored in FiveHour slot
        var utilization = monthly?.Utilization ?? 0;
        var percentText = monthly != null ? $"{utilization:F0}%" : "—%";
        var percentValue = (int)Math.Round(utilization);
        var percentValueClamped = Math.Max(1, Math.Min(100, percentValue));
        if (monthly == null) percentValueClamped = 0;

        var (barStyle, statusColor) = GetStatusVisuals(utilization, monthly != null);

        // Get total used and plan quota from provider
        long totalUsed = 0;
        long quotaLimit = 0;
        if (_providers.TryGetValue(CopilotWidgetId, out var provider) && provider is CopilotProvider cp)
        {
            totalUsed = cp.LastTotalUsed;
            quotaLimit = cp.QuotaLimit;
        }

        var usageText = quotaLimit > 0
            ? $"{totalUsed} / {quotaLimit} premium requests"
            : $"{totalUsed} premium requests";

        // Reset countdown
        var resetText = "";
        if (monthly?.ResetsAt != null)
        {
            var remaining = monthly.ResetsAt.Value - DateTimeOffset.Now;
            if (remaining.TotalSeconds > 0)
            {
                if (remaining.TotalDays >= 1)
                    resetText = $"Resets in {(int)remaining.TotalDays} days";
                else
                    resetText = $"Resets in {(int)remaining.TotalHours} hr {remaining.Minutes} min";
            }
        }

        var percentRemaining = Math.Max(1, 100 - percentValueClamped);

        return $$"""
        {
            "barStyle": "{{barStyle}}",
            "statusColor": "{{statusColor}}",
            "percentText": "{{percentText}}",
            "percentValue": {{percentValue}},
            "percentValueClamped": {{percentValueClamped}},
            "percentRemaining": {{percentRemaining}},
            "usageText": "{{usageText}}",
            "resetTime": "{{resetText}}",
            "size": "{{size}}"
        }
        """;
    }

    private static string FormatNumber(long number)
    {
        return number switch
        {
            >= 1_000_000_000 => $"{number / 1_000_000_000.0:F1}B",
            >= 1_000_000 => $"{number / 1_000_000.0:F1}M",
            >= 1_000 => $"{number / 1_000.0:F1}K",
            _ => number.ToString("N0")
        };
    }

    private static (string barStyle, string statusColor) GetStatusVisuals(double utilization, bool hasData)
    {
        if (!hasData) return ("emphasis", "default");
        return utilization switch
        {
            > 80 => ("attention", "attention"),
            > 60 => ("warning", "warning"),
            _ => ("accent", "accent")
        };
    }

    #region Adaptive Card Templates

    private static string GetTemplate(string definitionId, WidgetSize size)
    {
        var templateName = definitionId switch
        {
            ZaiWidgetId => size switch
            {
                WidgetSize.Small => "zai-small.json",
                WidgetSize.Medium => "zai-medium.json",
                WidgetSize.Large => "zai-large.json",
                _ => "zai-medium.json"
            },
            CopilotWidgetId => size switch
            {
                WidgetSize.Small => "copilot-small.json",
                WidgetSize.Medium => "copilot-medium.json",
                WidgetSize.Large => "copilot-large.json",
                _ => "copilot-medium.json"
            },
            QwenWidgetId => size switch
            {
                WidgetSize.Small => "qwen-small.json",
                WidgetSize.Medium => "qwen-medium.json",
                WidgetSize.Large => "qwen-large.json",
                _ => "qwen-medium.json"
            },
            _ => size switch
            {
                WidgetSize.Small => "claude-small.json",
                WidgetSize.Medium => "claude-medium.json",
                WidgetSize.Large => "claude-large.json",
                _ => "claude-medium.json"
            }
        };

        return AdaptiveCardTemplateLoader.LoadTemplate(templateName);
    }

    #endregion
}
