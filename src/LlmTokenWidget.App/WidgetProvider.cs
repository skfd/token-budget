using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Windows.Widgets;
using Microsoft.Windows.Widgets.Providers;
using LlmTokenWidget.Core;
using LlmTokenWidget.Providers.ClaudeCode;
using LlmTokenWidget.Providers.Copilot;
using LlmTokenWidget.Providers.Zai;

namespace LlmTokenWidget.App;

[ComVisible(true)]
[ComDefaultInterface(typeof(IWidgetProvider))]
[Guid("9F910C81-08A4-461F-93A6-96809C70A95D")] // CRITICAL: Must match Program.cs and manifest
public sealed class WidgetProvider : IWidgetProvider, IWidgetProvider2
{
    private const string ClaudeWidgetId = "Claude_Usage_Widget";
    private const string ZaiWidgetId = "Zai_Usage_Widget";
    private const string CopilotWidgetId = "Copilot_Usage_Widget";

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

        var gateway = new LlmTokenWidget.Core.HttpGateway();

        var claudeProvider = new ClaudeCodeLocalProvider(gateway);
        claudeProvider.DataChanged += OnProviderDataChanged;
        _providers[ClaudeWidgetId] = claudeProvider;

        var zaiProvider = new ZaiLocalProvider(gateway);
        zaiProvider.DataChanged += OnProviderDataChanged;
        _providers[ZaiWidgetId] = zaiProvider;

        _providers[CopilotWidgetId] = new CopilotProvider(gateway);
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

        // Status emoji based on utilization
        var statusEmoji = utilization switch
        {
            > 85 => "🔴",
            > 60 => "🟡",
            _ => fiveHour != null ? "🟢" : "⚪"
        };

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

        // Extra usage info
        var extraText = "";
        if (extra is { IsEnabled: true })
        {
            extraText = $"Overage: ${extra.UsedCredits / 100.0:F0}/${extra.MonthlyLimit / 100.0:F0} ({extra.Utilization:F0}%)";
        }

        var updatedTime = DateTimeOffset.Now.ToString("HH:mm:ss");

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
            "statusEmoji": "{{statusEmoji}}",
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

        var statusEmoji = utilization switch
        {
            > 85 => "🔴",
            > 60 => "🟡",
            _ => fiveHour != null ? "🟢" : "⚪"
        };

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

        return $$"""
        {
            "providerName": "{{providerName}}",
            "statusEmoji": "{{statusEmoji}}",
            "percentText": "{{percentText}}",
            "percentValue": {{percentValue}},
            "percentValueClamped": {{percentValueClamped}},
            "percentRemaining": {{percentRemaining}},
            "resetTime": "{{resetText}}",
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

        var statusEmoji = utilization switch
        {
            > 85 => "🔴",
            > 60 => "🟡",
            _ => monthly != null ? "🟢" : "⚪"
        };

        // Get total used from provider
        long totalUsed = 0;
        if (_providers.TryGetValue(CopilotWidgetId, out var provider) && provider is CopilotProvider cp)
            totalUsed = cp.LastTotalUsed;

        var usageText = $"{totalUsed} / 300 premium requests";

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
            "statusEmoji": "{{statusEmoji}}",
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

    #region Adaptive Card Templates

    private static string GetTemplate(string definitionId, WidgetSize size)
    {
        return definitionId switch
        {
            ZaiWidgetId => size switch
            {
                WidgetSize.Small => ZaiSmallTemplate,
                WidgetSize.Medium => ZaiMediumTemplate,
                WidgetSize.Large => ZaiLargeTemplate,
                _ => ZaiMediumTemplate
            },
            CopilotWidgetId => size switch
            {
                WidgetSize.Small => CopilotSmallTemplate,
                WidgetSize.Medium => CopilotMediumTemplate,
                WidgetSize.Large => CopilotLargeTemplate,
                _ => CopilotMediumTemplate
            },
            _ => size switch
            {
                WidgetSize.Small => SmallTemplate,
                WidgetSize.Medium => MediumTemplate,
                WidgetSize.Large => LargeTemplate,
                _ => MediumTemplate
            }
        };
    }

    // 1x1 blue (#6699FF) pixel for progress bar fill
    private const string BluePx = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPj/HwADBwIAMCbHYQAAAABJRU5ErkJggg==";
    // 1x1 dark gray (#3D3D3D) pixel for progress bar track
    private const string GrayPx = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADklEQVR4nGI9c+Y/AwAEVAHJAl2DJQAAAABJRU5ErkJggg==";

    private const string SmallTemplate = """
    {
        "type": "AdaptiveCard",
        "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
        "version": "1.5",
        "body": [
            {
                "type": "TextBlock",
                "text": "Plan usage limits",
                "weight": "bolder",
                "size": "small"
            },
            {
                "type": "ColumnSet",
                "spacing": "small",
                "columns": [
                    {
                        "type": "Column",
                        "width": "stretch",
                        "items": [
                            {
                                "type": "ColumnSet",
                                "spacing": "none",
                                "columns": [
                                    {
                                        "type": "Column",
                                        "width": "${percentValueClamped}",
                                        "items": [],
                                        "backgroundImage": {
                                            "url": "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPj/HwADBwIAMCbHYQAAAABJRU5ErkJggg==",
                                            "fillMode": "repeatHorizontally"
                                        },
                                        "minHeight": "6px"
                                    },
                                    {
                                        "type": "Column",
                                        "width": "${percentRemaining}",
                                        "items": [],
                                        "backgroundImage": {
                                            "url": "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADklEQVR4nGI9c+Y/AwAEVAHJAl2DJQAAAABJRU5ErkJggg==",
                                            "fillMode": "repeatHorizontally"
                                        },
                                        "minHeight": "6px"
                                    }
                                ]
                            }
                        ]
                    },
                    {
                        "type": "Column",
                        "width": "auto",
                        "items": [
                            {
                                "type": "TextBlock",
                                "text": "${percentText} used",
                                "size": "small",
                                "isSubtle": true
                            }
                        ],
                        "verticalContentAlignment": "center"
                    }
                ]
            },
            {
                "type": "TextBlock",
                "text": "${resetTime}",
                "size": "small",
                "isSubtle": true,
                "spacing": "none"
            }
        ]
    }
    """;

    private const string MediumTemplate = """
    {
        "type": "AdaptiveCard",
        "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
        "version": "1.5",
        "body": [
            {
                "type": "TextBlock",
                "text": "Plan usage limits",
                "weight": "bolder",
                "size": "medium"
            },
            {
                "type": "TextBlock",
                "text": "Current session",
                "weight": "bolder",
                "size": "small",
                "spacing": "small"
            },
            {
                "type": "TextBlock",
                "text": "${resetTime}",
                "size": "small",
                "isSubtle": true,
                "spacing": "none"
            },
            {
                "type": "ColumnSet",
                "spacing": "small",
                "columns": [
                    {
                        "type": "Column",
                        "width": "stretch",
                        "items": [
                            {
                                "type": "ColumnSet",
                                "spacing": "none",
                                "columns": [
                                    {
                                        "type": "Column",
                                        "width": "${percentValueClamped}",
                                        "items": [],
                                        "backgroundImage": {
                                            "url": "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPj/HwADBwIAMCbHYQAAAABJRU5ErkJggg==",
                                            "fillMode": "repeatHorizontally"
                                        },
                                        "minHeight": "6px"
                                    },
                                    {
                                        "type": "Column",
                                        "width": "${percentRemaining}",
                                        "items": [],
                                        "backgroundImage": {
                                            "url": "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADklEQVR4nGI9c+Y/AwAEVAHJAl2DJQAAAABJRU5ErkJggg==",
                                            "fillMode": "repeatHorizontally"
                                        },
                                        "minHeight": "6px"
                                    }
                                ]
                            }
                        ],
                        "verticalContentAlignment": "center"
                    },
                    {
                        "type": "Column",
                        "width": "auto",
                        "items": [
                            {
                                "type": "TextBlock",
                                "text": "${percentText} used",
                                "size": "small",
                                "isSubtle": true
                            }
                        ],
                        "verticalContentAlignment": "center"
                    }
                ]
            },
            {
                "type": "TextBlock",
                "text": "${extraUsage}",
                "size": "small",
                "isSubtle": true,
                "spacing": "none",
                "$when": "${extraUsage != ''}"
            },
            {
                "type": "Container",
                "spacing": "small",
                "separator": true,
                "items": [
                    {
                        "type": "TextBlock",
                        "text": "Weekly limits",
                        "weight": "bolder",
                        "size": "small",
                        "spacing": "small"
                    },
                    {
                        "type": "TextBlock",
                        "text": "All models",
                        "weight": "bolder",
                        "size": "small",
                        "spacing": "small"
                    },
                    {
                        "type": "TextBlock",
                        "text": "${sevenDayReset}",
                        "size": "small",
                        "isSubtle": true,
                        "spacing": "none"
                    },
                    {
                        "type": "ColumnSet",
                        "spacing": "small",
                        "columns": [
                            {
                                "type": "Column",
                                "width": "stretch",
                                "items": [
                                    {
                                        "type": "ColumnSet",
                                        "spacing": "none",
                                        "columns": [
                                            {
                                                "type": "Column",
                                                "width": "${sevenDayValueClamped}",
                                                "items": [],
                                                "backgroundImage": {
                                                    "url": "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPj/HwADBwIAMCbHYQAAAABJRU5ErkJggg==",
                                                    "fillMode": "repeatHorizontally"
                                                },
                                                "minHeight": "6px"
                                            },
                                            {
                                                "type": "Column",
                                                "width": "${sevenDayRemaining}",
                                                "items": [],
                                                "backgroundImage": {
                                                    "url": "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADklEQVR4nGI9c+Y/AwAEVAHJAl2DJQAAAABJRU5ErkJggg==",
                                                    "fillMode": "repeatHorizontally"
                                                },
                                                "minHeight": "6px"
                                            }
                                        ]
                                    }
                                ],
                                "verticalContentAlignment": "center"
                            },
                            {
                                "type": "Column",
                                "width": "auto",
                                "items": [
                                    {
                                        "type": "TextBlock",
                                        "text": "${sevenDayPercent} used",
                                        "size": "small",
                                        "isSubtle": true
                                    }
                                ],
                                "verticalContentAlignment": "center"
                            }
                        ]
                    }
                ]
            }
        ]
    }
    """;

    private const string LargeTemplate = """
    {
        "type": "AdaptiveCard",
        "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
        "version": "1.5",
        "body": [
            {
                "type": "TextBlock",
                "text": "Plan usage limits",
                "weight": "bolder",
                "size": "medium"
            },
            {
                "type": "TextBlock",
                "text": "Current session",
                "weight": "bolder",
                "size": "small",
                "spacing": "small"
            },
            {
                "type": "TextBlock",
                "text": "${resetTime}",
                "size": "small",
                "isSubtle": true,
                "spacing": "none"
            },
            {
                "type": "ColumnSet",
                "spacing": "small",
                "columns": [
                    {
                        "type": "Column",
                        "width": "stretch",
                        "items": [
                            {
                                "type": "ColumnSet",
                                "spacing": "none",
                                "columns": [
                                    {
                                        "type": "Column",
                                        "width": "${percentValueClamped}",
                                        "items": [],
                                        "backgroundImage": {
                                            "url": "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPj/HwADBwIAMCbHYQAAAABJRU5ErkJggg==",
                                            "fillMode": "repeatHorizontally"
                                        },
                                        "minHeight": "8px"
                                    },
                                    {
                                        "type": "Column",
                                        "width": "${percentRemaining}",
                                        "items": [],
                                        "backgroundImage": {
                                            "url": "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADklEQVR4nGI9c+Y/AwAEVAHJAl2DJQAAAABJRU5ErkJggg==",
                                            "fillMode": "repeatHorizontally"
                                        },
                                        "minHeight": "8px"
                                    }
                                ]
                            }
                        ],
                        "verticalContentAlignment": "center"
                    },
                    {
                        "type": "Column",
                        "width": "auto",
                        "items": [
                            {
                                "type": "TextBlock",
                                "text": "${percentText} used",
                                "size": "small",
                                "isSubtle": true
                            }
                        ],
                        "verticalContentAlignment": "center"
                    }
                ]
            },
            {
                "type": "TextBlock",
                "text": "${extraUsage}",
                "size": "small",
                "isSubtle": true,
                "spacing": "none",
                "$when": "${extraUsage != ''}"
            },
            {
                "type": "Container",
                "spacing": "small",
                "separator": true,
                "items": [
                    {
                        "type": "TextBlock",
                        "text": "Weekly limits",
                        "weight": "bolder",
                        "size": "small",
                        "spacing": "small"
                    },
                    {
                        "type": "TextBlock",
                        "text": "All models",
                        "weight": "bolder",
                        "size": "small",
                        "spacing": "small"
                    },
                    {
                        "type": "TextBlock",
                        "text": "${sevenDayReset}",
                        "size": "small",
                        "isSubtle": true,
                        "spacing": "none"
                    },
                    {
                        "type": "ColumnSet",
                        "spacing": "small",
                        "columns": [
                            {
                                "type": "Column",
                                "width": "stretch",
                                "items": [
                                    {
                                        "type": "ColumnSet",
                                        "spacing": "none",
                                        "columns": [
                                            {
                                                "type": "Column",
                                                "width": "${sevenDayValueClamped}",
                                                "items": [],
                                                "backgroundImage": {
                                                    "url": "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPj/HwADBwIAMCbHYQAAAABJRU5ErkJggg==",
                                                    "fillMode": "repeatHorizontally"
                                                },
                                                "minHeight": "6px"
                                            },
                                            {
                                                "type": "Column",
                                                "width": "${sevenDayRemaining}",
                                                "items": [],
                                                "backgroundImage": {
                                                    "url": "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADklEQVR4nGI9c+Y/AwAEVAHJAl2DJQAAAABJRU5ErkJggg==",
                                                    "fillMode": "repeatHorizontally"
                                                },
                                                "minHeight": "6px"
                                            }
                                        ]
                                    }
                                ],
                                "verticalContentAlignment": "center"
                            },
                            {
                                "type": "Column",
                                "width": "auto",
                                "items": [
                                    {
                                        "type": "TextBlock",
                                        "text": "${sevenDayPercent} used",
                                        "size": "small",
                                        "isSubtle": true
                                    }
                                ],
                                "verticalContentAlignment": "center"
                            }
                        ]
                    }
                ]
            },
            {
                "type": "Container",
                "spacing": "small",
                "separator": true,
                "items": [
                    {
                        "type": "TextBlock",
                        "text": "Token breakdown",
                        "weight": "bolder",
                        "size": "small",
                        "spacing": "small"
                    },
                    {
                        "type": "ColumnSet",
                        "spacing": "small",
                        "columns": [
                            {
                                "type": "Column",
                                "width": "stretch",
                                "items": [
                                    {
                                        "type": "TextBlock",
                                        "text": "Input",
                                        "size": "small",
                                        "isSubtle": true
                                    },
                                    {
                                        "type": "TextBlock",
                                        "text": "${inputTokens}",
                                        "size": "small",
                                        "spacing": "none"
                                    }
                                ]
                            },
                            {
                                "type": "Column",
                                "width": "stretch",
                                "items": [
                                    {
                                        "type": "TextBlock",
                                        "text": "Output",
                                        "size": "small",
                                        "isSubtle": true
                                    },
                                    {
                                        "type": "TextBlock",
                                        "text": "${outputTokens}",
                                        "size": "small",
                                        "spacing": "none"
                                    }
                                ]
                            },
                            {
                                "type": "Column",
                                "width": "stretch",
                                "items": [
                                    {
                                        "type": "TextBlock",
                                        "text": "Cache W",
                                        "size": "small",
                                        "isSubtle": true
                                    },
                                    {
                                        "type": "TextBlock",
                                        "text": "${cacheCreation}",
                                        "size": "small",
                                        "spacing": "none"
                                    }
                                ]
                            },
                            {
                                "type": "Column",
                                "width": "stretch",
                                "items": [
                                    {
                                        "type": "TextBlock",
                                        "text": "Cache R",
                                        "size": "small",
                                        "isSubtle": true
                                    },
                                    {
                                        "type": "TextBlock",
                                        "text": "${cacheRead}",
                                        "size": "small",
                                        "spacing": "none"
                                    }
                                ]
                            }
                        ]
                    }
                ]
            }
        ]
    }
    """;

    // 1x1 teal (#00BCF2) pixel for Z.ai progress bar fill - Windows 11 accent
    private const string TealPx = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M/wHwAEBgIAbw0VCQAAAABJRU5ErkJggg==";

    private const string ZaiSmallTemplate = """
    {
        "type": "AdaptiveCard",
        "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
        "version": "1.5",
        "body": [
            {
                "type": "TextBlock",
                "text": "${providerName} quota",
                "weight": "bolder",
                "size": "small"
            },
            {
                "type": "ColumnSet",
                "spacing": "small",
                "columns": [
                    {
                        "type": "Column",
                        "width": "stretch",
                        "items": [
                            {
                                "type": "ColumnSet",
                                "spacing": "none",
                                "columns": [
                                    {
                                        "type": "Column",
                                        "width": "${percentValueClamped}",
                                        "items": [],
                                        "backgroundImage": {
                                            "url": "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M/wHwAEBgIAbw0VCQAAAABJRU5ErkJggg==",
                                            "fillMode": "repeatHorizontally"
                                        },
                                        "minHeight": "6px"
                                    },
                                    {
                                        "type": "Column",
                                        "width": "${percentRemaining}",
                                        "items": [],
                                        "backgroundImage": {
                                            "url": "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADklEQVR4nGI9c+Y/AwAEVAHJAl2DJQAAAABJRU5ErkJggg==",
                                            "fillMode": "repeatHorizontally"
                                        },
                                        "minHeight": "6px"
                                    }
                                ]
                            }
                        ]
                    },
                    {
                        "type": "Column",
                        "width": "auto",
                        "items": [
                            {
                                "type": "TextBlock",
                                "text": "${percentText} used",
                                "size": "small",
                                "isSubtle": true
                            }
                        ],
                        "verticalContentAlignment": "center"
                    }
                ]
            },
            {
                "type": "TextBlock",
                "text": "${resetTime}",
                "size": "small",
                "isSubtle": true,
                "spacing": "none"
            }
        ]
    }
    """;

    private const string ZaiMediumTemplate = """
    {
        "type": "AdaptiveCard",
        "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
        "version": "1.5",
        "body": [
            {
                "type": "TextBlock",
                "text": "5 Hours Quota",
                "weight": "bolder",
                "size": "medium"
            },
            {
                "type": "TextBlock",
                "text": "${resetTime}",
                "size": "small",
                "isSubtle": true,
                "spacing": "none"
            },
            {
                "type": "ColumnSet",
                "spacing": "small",
                "columns": [
                    {
                        "type": "Column",
                        "width": "stretch",
                        "items": [
                            {
                                "type": "ColumnSet",
                                "spacing": "none",
                                "columns": [
                                    {
                                        "type": "Column",
                                        "width": "${percentValueClamped}",
                                        "items": [],
                                        "backgroundImage": {
                                            "url": "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M/wHwAEBgIAbw0VCQAAAABJRU5ErkJggg==",
                                            "fillMode": "repeatHorizontally"
                                        },
                                        "minHeight": "6px"
                                    },
                                    {
                                        "type": "Column",
                                        "width": "${percentRemaining}",
                                        "items": [],
                                        "backgroundImage": {
                                            "url": "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADklEQVR4nGI9c+Y/AwAEVAHJAl2DJQAAAABJRU5ErkJggg==",
                                            "fillMode": "repeatHorizontally"
                                        },
                                        "minHeight": "6px"
                                    }
                                ]
                            }
                        ],
                        "verticalContentAlignment": "center"
                    },
                    {
                        "type": "Column",
                        "width": "auto",
                        "items": [
                            {
                                "type": "TextBlock",
                                "text": "${percentText} used",
                                "size": "small",
                                "isSubtle": true
                            }
                        ],
                        "verticalContentAlignment": "center"
                    }
                ]
            },
            {
                "type": "Container",
                "spacing": "small",
                "separator": true,
                "items": [
                    {
                        "type": "TextBlock",
                        "text": "Weekly Quota",
                        "weight": "bolder",
                        "size": "small",
                        "spacing": "small"
                    },
                    {
                        "type": "TextBlock",
                        "text": "${weeklyReset}",
                        "size": "small",
                        "isSubtle": true,
                        "spacing": "none"
                    },
                    {
                        "type": "ColumnSet",
                        "spacing": "small",
                        "columns": [
                            {
                                "type": "Column",
                                "width": "stretch",
                                "items": [
                                    {
                                        "type": "ColumnSet",
                                        "spacing": "none",
                                        "columns": [
                                            {
                                                "type": "Column",
                                                "width": "${weeklyValueClamped}",
                                                "items": [],
                                                "backgroundImage": {
                                                    "url": "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M/wHwAEBgIAbw0VCQAAAABJRU5ErkJggg==",
                                                    "fillMode": "repeatHorizontally"
                                                },
                                                "minHeight": "6px"
                                            },
                                            {
                                                "type": "Column",
                                                "width": "${weeklyRemaining}",
                                                "items": [],
                                                "backgroundImage": {
                                                    "url": "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADklEQVR4nGI9c+Y/AwAEVAHJAl2DJQAAAABJRU5ErkJggg==",
                                                    "fillMode": "repeatHorizontally"
                                                },
                                                "minHeight": "6px"
                                            }
                                        ]
                                    }
                                ],
                                "verticalContentAlignment": "center"
                            },
                            {
                                "type": "Column",
                                "width": "auto",
                                "items": [
                                    {
                                        "type": "TextBlock",
                                        "text": "${weeklyPercent} used",
                                        "size": "small",
                                        "isSubtle": true
                                    }
                                ],
                                "verticalContentAlignment": "center"
                            }
                        ]
                    }
                ]
            }
        ]
    }
    """;

    private const string ZaiLargeTemplate = """
    {
        "type": "AdaptiveCard",
        "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
        "version": "1.5",
        "body": [
            {
                "type": "TextBlock",
                "text": "5 Hours Quota",
                "weight": "bolder",
                "size": "medium"
            },
            {
                "type": "TextBlock",
                "text": "${resetTime}",
                "size": "small",
                "isSubtle": true,
                "spacing": "none"
            },
            {
                "type": "ColumnSet",
                "spacing": "small",
                "columns": [
                    {
                        "type": "Column",
                        "width": "stretch",
                        "items": [
                            {
                                "type": "ColumnSet",
                                "spacing": "none",
                                "columns": [
                                    {
                                        "type": "Column",
                                        "width": "${percentValueClamped}",
                                        "items": [],
                                        "backgroundImage": {
                                            "url": "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M/wHwAEBgIAbw0VCQAAAABJRU5ErkJggg==",
                                            "fillMode": "repeatHorizontally"
                                        },
                                        "minHeight": "8px"
                                    },
                                    {
                                        "type": "Column",
                                        "width": "${percentRemaining}",
                                        "items": [],
                                        "backgroundImage": {
                                            "url": "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADklEQVR4nGI9c+Y/AwAEVAHJAl2DJQAAAABJRU5ErkJggg==",
                                            "fillMode": "repeatHorizontally"
                                        },
                                        "minHeight": "8px"
                                    }
                                ]
                            }
                        ],
                        "verticalContentAlignment": "center"
                    },
                    {
                        "type": "Column",
                        "width": "auto",
                        "items": [
                            {
                                "type": "TextBlock",
                                "text": "${percentText} used",
                                "size": "small",
                                "isSubtle": true
                            }
                        ],
                        "verticalContentAlignment": "center"
                    }
                ]
            },
            {
                "type": "Container",
                "spacing": "small",
                "separator": true,
                "items": [
                    {
                        "type": "TextBlock",
                        "text": "Weekly Quota",
                        "weight": "bolder",
                        "size": "small",
                        "spacing": "small"
                    },
                    {
                        "type": "TextBlock",
                        "text": "${weeklyReset}",
                        "size": "small",
                        "isSubtle": true,
                        "spacing": "none"
                    },
                    {
                        "type": "ColumnSet",
                        "spacing": "small",
                        "columns": [
                            {
                                "type": "Column",
                                "width": "stretch",
                                "items": [
                                    {
                                        "type": "ColumnSet",
                                        "spacing": "none",
                                        "columns": [
                                            {
                                                "type": "Column",
                                                "width": "${weeklyValueClamped}",
                                                "items": [],
                                                "backgroundImage": {
                                                    "url": "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M/wHwAEBgIAbw0VCQAAAABJRU5ErkJggg==",
                                                    "fillMode": "repeatHorizontally"
                                                },
                                                "minHeight": "6px"
                                            },
                                            {
                                                "type": "Column",
                                                "width": "${weeklyRemaining}",
                                                "items": [],
                                                "backgroundImage": {
                                                    "url": "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADklEQVR4nGI9c+Y/AwAEVAHJAl2DJQAAAABJRU5ErkJggg==",
                                                    "fillMode": "repeatHorizontally"
                                                },
                                                "minHeight": "6px"
                                            }
                                        ]
                                    }
                                ],
                                "verticalContentAlignment": "center"
                            },
                            {
                                "type": "Column",
                                "width": "auto",
                                "items": [
                                    {
                                        "type": "TextBlock",
                                        "text": "${weeklyPercent} used",
                                        "size": "small",
                                        "isSubtle": true
                                    }
                                ],
                                "verticalContentAlignment": "center"
                            }
                        ]
                    }
                ]
            },
            {
                "type": "Container",
                "spacing": "small",
                "separator": true,
                "items": [
                    {
                        "type": "TextBlock",
                        "text": "Token breakdown",
                        "weight": "bolder",
                        "size": "small",
                        "spacing": "small"
                    },
                    {
                        "type": "TextBlock",
                        "text": "${sessionCount} sessions • ${messageCount} messages",
                        "size": "small",
                        "isSubtle": true,
                        "spacing": "none"
                    },
                    {
                        "type": "ColumnSet",
                        "spacing": "small",
                        "columns": [
                            {
                                "type": "Column",
                                "width": "stretch",
                                "items": [
                                    {
                                        "type": "TextBlock",
                                        "text": "Input",
                                        "size": "small",
                                        "isSubtle": true
                                    },
                                    {
                                        "type": "TextBlock",
                                        "text": "${inputTokens}",
                                        "size": "small",
                                        "spacing": "none"
                                    }
                                ]
                            },
                            {
                                "type": "Column",
                                "width": "stretch",
                                "items": [
                                    {
                                        "type": "TextBlock",
                                        "text": "Output",
                                        "size": "small",
                                        "isSubtle": true
                                    },
                                    {
                                        "type": "TextBlock",
                                        "text": "${outputTokens}",
                                        "size": "small",
                                        "spacing": "none"
                                    }
                                ]
                            },
                            {
                                "type": "Column",
                                "width": "stretch",
                                "items": [
                                    {
                                        "type": "TextBlock",
                                        "text": "Cache W",
                                        "size": "small",
                                        "isSubtle": true
                                    },
                                    {
                                        "type": "TextBlock",
                                        "text": "${cacheWrite}",
                                        "size": "small",
                                        "spacing": "none"
                                    }
                                ]
                            },
                            {
                                "type": "Column",
                                "width": "stretch",
                                "items": [
                                    {
                                        "type": "TextBlock",
                                        "text": "Cache R",
                                        "size": "small",
                                        "isSubtle": true
                                    },
                                    {
                                        "type": "TextBlock",
                                        "text": "${cacheRead}",
                                        "size": "small",
                                        "spacing": "none"
                                    }
                                ]
                            }
                        ]
                    }
                ]
            }
        ]
    }
    """;

    // 1x1 green (#2EA043) pixel for Copilot progress bar fill
    private const string GreenPx = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVR4nGPQW+AMAAIRARK0QUTlAAAAAElFTkSuQmCC";

    private const string CopilotSmallTemplate = """
    {
        "type": "AdaptiveCard",
        "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
        "version": "1.5",
        "body": [
            {
                "type": "TextBlock",
                "text": "Copilot monthly quota",
                "weight": "bolder",
                "size": "small"
            },
            {
                "type": "ColumnSet",
                "spacing": "small",
                "columns": [
                    {
                        "type": "Column",
                        "width": "stretch",
                        "items": [
                            {
                                "type": "ColumnSet",
                                "spacing": "none",
                                "columns": [
                                    {
                                        "type": "Column",
                                        "width": "${percentValueClamped}",
                                        "items": [],
                                        "backgroundImage": {
                                            "url": "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVR4nGPQW+AMAAIRARK0QUTlAAAAAElFTkSuQmCC",
                                            "fillMode": "repeatHorizontally"
                                        },
                                        "minHeight": "6px"
                                    },
                                    {
                                        "type": "Column",
                                        "width": "${percentRemaining}",
                                        "items": [],
                                        "backgroundImage": {
                                            "url": "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADklEQVR4nGI9c+Y/AwAEVAHJAl2DJQAAAABJRU5ErkJggg==",
                                            "fillMode": "repeatHorizontally"
                                        },
                                        "minHeight": "6px"
                                    }
                                ]
                            }
                        ]
                    },
                    {
                        "type": "Column",
                        "width": "auto",
                        "items": [
                            {
                                "type": "TextBlock",
                                "text": "${percentText} used",
                                "size": "small",
                                "isSubtle": true
                            }
                        ],
                        "verticalContentAlignment": "center"
                    }
                ]
            },
            {
                "type": "TextBlock",
                "text": "${resetTime}",
                "size": "small",
                "isSubtle": true,
                "spacing": "none"
            }
        ]
    }
    """;

    private const string CopilotMediumTemplate = """
    {
        "type": "AdaptiveCard",
        "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
        "version": "1.5",
        "body": [
            {
                "type": "TextBlock",
                "text": "Copilot Monthly Quota",
                "weight": "bolder",
                "size": "medium"
            },
            {
                "type": "TextBlock",
                "text": "${usageText}",
                "size": "small",
                "spacing": "small"
            },
            {
                "type": "TextBlock",
                "text": "${resetTime}",
                "size": "small",
                "isSubtle": true,
                "spacing": "none"
            },
            {
                "type": "ColumnSet",
                "spacing": "small",
                "columns": [
                    {
                        "type": "Column",
                        "width": "stretch",
                        "items": [
                            {
                                "type": "ColumnSet",
                                "spacing": "none",
                                "columns": [
                                    {
                                        "type": "Column",
                                        "width": "${percentValueClamped}",
                                        "items": [],
                                        "backgroundImage": {
                                            "url": "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVR4nGPQW+AMAAIRARK0QUTlAAAAAElFTkSuQmCC",
                                            "fillMode": "repeatHorizontally"
                                        },
                                        "minHeight": "6px"
                                    },
                                    {
                                        "type": "Column",
                                        "width": "${percentRemaining}",
                                        "items": [],
                                        "backgroundImage": {
                                            "url": "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADklEQVR4nGI9c+Y/AwAEVAHJAl2DJQAAAABJRU5ErkJggg==",
                                            "fillMode": "repeatHorizontally"
                                        },
                                        "minHeight": "6px"
                                    }
                                ]
                            }
                        ],
                        "verticalContentAlignment": "center"
                    },
                    {
                        "type": "Column",
                        "width": "auto",
                        "items": [
                            {
                                "type": "TextBlock",
                                "text": "${percentText} used",
                                "size": "small",
                                "isSubtle": true
                            }
                        ],
                        "verticalContentAlignment": "center"
                    }
                ]
            }
        ]
    }
    """;

    private const string CopilotLargeTemplate = """
    {
        "type": "AdaptiveCard",
        "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
        "version": "1.5",
        "body": [
            {
                "type": "TextBlock",
                "text": "Copilot Monthly Quota",
                "weight": "bolder",
                "size": "medium"
            },
            {
                "type": "TextBlock",
                "text": "${usageText}",
                "size": "small",
                "spacing": "small"
            },
            {
                "type": "TextBlock",
                "text": "${resetTime}",
                "size": "small",
                "isSubtle": true,
                "spacing": "none"
            },
            {
                "type": "ColumnSet",
                "spacing": "small",
                "columns": [
                    {
                        "type": "Column",
                        "width": "stretch",
                        "items": [
                            {
                                "type": "ColumnSet",
                                "spacing": "none",
                                "columns": [
                                    {
                                        "type": "Column",
                                        "width": "${percentValueClamped}",
                                        "items": [],
                                        "backgroundImage": {
                                            "url": "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVR4nGPQW+AMAAIRARK0QUTlAAAAAElFTkSuQmCC",
                                            "fillMode": "repeatHorizontally"
                                        },
                                        "minHeight": "8px"
                                    },
                                    {
                                        "type": "Column",
                                        "width": "${percentRemaining}",
                                        "items": [],
                                        "backgroundImage": {
                                            "url": "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADklEQVR4nGI9c+Y/AwAEVAHJAl2DJQAAAABJRU5ErkJggg==",
                                            "fillMode": "repeatHorizontally"
                                        },
                                        "minHeight": "8px"
                                    }
                                ]
                            }
                        ],
                        "verticalContentAlignment": "center"
                    },
                    {
                        "type": "Column",
                        "width": "auto",
                        "items": [
                            {
                                "type": "TextBlock",
                                "text": "${percentText} used",
                                "size": "small",
                                "isSubtle": true
                            }
                        ],
                        "verticalContentAlignment": "center"
                    }
                ]
            }
        ]
    }
    """;

    #endregion
}
