using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Windows.Widgets;
using Microsoft.Windows.Widgets.Providers;
using LlmTokenWidget.Core;
using LlmTokenWidget.Providers.ClaudeCode;

namespace LlmTokenWidget.App;

[ComVisible(true)]
[ComDefaultInterface(typeof(IWidgetProvider))]
[Guid("9F910C81-08A4-461F-93A6-96809C70A95D")] // CRITICAL: Must match Program.cs and manifest
public sealed class WidgetProvider : IWidgetProvider, IWidgetProvider2
{
    private readonly Dictionary<string, WidgetState> _activeWidgets = new();
    private readonly ClaudeCodeLocalProvider _provider;
    private Timer? _refreshTimer;
    private readonly object _lock = new();

    /// <summary>Per-widget tracking state.</summary>
    private sealed class WidgetState
    {
        public string DefinitionId { get; set; } = "";
        public WidgetSize Size { get; set; }
    }

    public WidgetProvider()
    {
        System.Diagnostics.Debug.WriteLine("WidgetProvider constructor called");
        _provider = new ClaudeCodeLocalProvider();
        _provider.DataChanged += OnProviderDataChanged;
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
            _refreshTimer ??= new Timer(_ => RefreshAllWidgets(),
                null,
                _provider.PollingInterval,
                _provider.PollingInterval);
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

            var usageTask = _provider.FetchUsageAsync(CancellationToken.None);
            // Cooldown estimation removed (Phase 3 cleanup)
            var usage = usageTask.GetAwaiter().GetResult();

            var templateJson = GetTemplate(state.Size);
            var dataJson = BuildDataJson(usage, state.Size); // Removed cooldown arg

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

    private string BuildDataJson(UsageSnapshot usage, WidgetSize size)
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
                    ? $"Resets in {remaining.Hours}h {remaining.Minutes}m"
                    : $"Resets in {remaining.Minutes}m";
            }
        }

        // 7-day info line
        var sevenDayText = "";
        if (sevenDay != null)
        {
            sevenDayText = $"7d: {sevenDay.Utilization:F0}%";
            if (sevenDay.ResetsAt != null)
            {
                var remaining7d = sevenDay.ResetsAt.Value - DateTimeOffset.Now;
                if (remaining7d.TotalHours > 0)
                {
                    sevenDayText += $" (resets {remaining7d.Days}d {remaining7d.Hours}h)";
                }
            }
        }

        // Extra usage info
        var extraText = "";
        if (extra is { IsEnabled: true })
        {
            extraText = $"Overage: ${extra.UsedCredits:F0}/${extra.MonthlyLimit:F0} ({extra.Utilization:F0}%)";
        }

        var updatedTime = DateTimeOffset.Now.ToString("HH:mm:ss");

        var planName = "Claude Code";

        // Extract live status data if available
        var live = usage.LiveStatus;
        var costText = live?.CostUsd.HasValue == true ? $"${live.CostUsd.Value:F2}" : "";
        var modelText = !string.IsNullOrEmpty(live?.ModelName) ? live.ModelName : "Claude Code";
        var contextText = live?.ContextWindowUsedPercent.HasValue == true ? $"Ctx: {live.ContextWindowUsedPercent.Value:F1}%" : "";

        return $$"""
        {
            "statusEmoji": "{{statusEmoji}}",
            "percentText": "{{percentText}}",
            "percentValue": {{percentValue}},
            "percentValueClamped": {{percentValueClamped}},
            "totalTokens": "{{FormatNumber(total.Total)}}",
            "inputTokens": "{{FormatNumber(total.InputTokens)}}",
            "outputTokens": "{{FormatNumber(total.OutputTokens)}}",
            "cacheCreation": "{{FormatNumber(total.CacheCreationTokens)}}",
            "cacheRead": "{{FormatNumber(total.CacheReadTokens)}}",
            "tokenLimit": "—",
            "windowTokens": "—",
            "messageCount": "{{usage.MessageCount}}",
            "resetTime": "{{resetText}}",
            "sevenDay": "{{sevenDayText}}",
            "extraUsage": "{{extraText}}",
            "updatedTime": "{{updatedTime}}",
            "planName": "{{planName}}",
            "cost": "{{costText}}",
            "model": "{{modelText}}",
            "context": "{{contextText}}",
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

    private static string GetTemplate(WidgetSize size) => size switch
    {
        WidgetSize.Small => SmallTemplate,
        WidgetSize.Medium => MediumTemplate,
        WidgetSize.Large => LargeTemplate,
        _ => MediumTemplate
    };

    private const string SmallTemplate = """
    {
        "type": "AdaptiveCard",
        "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
        "version": "1.5",
        "body": [
            {
                "type": "Container",
                "items": [
                    {
                        "type": "TextBlock",
                        "text": "${statusEmoji} ${model}",
                        "weight": "bolder",
                        "size": "medium"
                    },
                    {
                        "type": "TextBlock",
                        "text": "${windowTokens} / ${tokenLimit} ${cost}",
                        "size": "small",
                        "spacing": "small"
                    },
                    {
                        "type": "ColumnSet",
                        "columns": [
                            {
                                "type": "Column",
                                "width": "${percentValueClamped}",
                                "items": [
                                    {
                                        "type": "TextBlock",
                                        "text": " ",
                                        "size": "small",
                                        "height": "stretch"
                                    }
                                ],
                                "backgroundImage": {
                                    "url": "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPj/HwADBwIAMCbHYQAAAABJRU5ErkJggg==",
                                    "fillMode": "repeatHorizontally"
                                },
                                "minHeight": "6px"
                            },
                            {
                                "type": "Column",
                                "width": "stretch",
                                "items": []
                            }
                        ],
                        "spacing": "small"
                    },
                    {
                        "type": "TextBlock",
                        "text": "${percentText} used",
                        "size": "small",
                        "isSubtle": true,
                        "spacing": "small"
                    }
                ]
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
                "type": "Container",
                "items": [
                    {
                        "type": "ColumnSet",
                        "columns": [
                            {
                                "type": "Column",
                                "width": "stretch",
                                "items": [
                                    {
                                        "type": "TextBlock",
                                        "text": "${statusEmoji} ${model}",
                                        "weight": "bolder",
                                        "size": "medium"
                                    }
                                ]
                            },
                            {
                                "type": "Column",
                                "width": "auto",
                                "items": [
                                    {
                                        "type": "TextBlock",
                                        "text": "${percentText}",
                                        "weight": "bolder",
                                        "size": "medium",
                                        "color": "accent"
                                    }
                                ]
                            }
                        ]
                    },
                    {
                        "type": "ColumnSet",
                        "columns": [
                            {
                                "type": "Column",
                                "width": "${percentValueClamped}",
                                "items": [
                                    {
                                        "type": "TextBlock",
                                        "text": " ",
                                        "size": "small"
                                    }
                                ],
                                "backgroundImage": {
                                    "url": "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPj/HwADBwIAMCbHYQAAAABJRU5ErkJggg==",
                                    "fillMode": "repeatHorizontally"
                                },
                                "minHeight": "6px"
                            },
                            {
                                "type": "Column",
                                "width": "stretch",
                                "items": []
                            }
                        ],
                        "spacing": "small"
                    },
                    {
                        "type": "TextBlock",
                        "text": "${planName} · 5h: ${percentText}  ${sevenDay}",
                        "size": "small",
                        "isSubtle": true,
                        "spacing": "small"
                    },
                    {
                        "type": "ColumnSet",
                        "spacing": "medium",
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
                                        "weight": "bolder",
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
                                        "weight": "bolder",
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
                                        "weight": "bolder",
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
                                        "weight": "bolder",
                                        "spacing": "none"
                                    }
                                ]
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
                        "type": "TextBlock",
                        "text": "${resetTime} · Updated ${updatedTime}",
                        "size": "small",
                        "isSubtle": true,
                        "spacing": "medium"
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
                "type": "Container",
                "items": [
                    {
                        "type": "ColumnSet",
                        "columns": [
                            {
                                "type": "Column",
                                "width": "stretch",
                                "items": [
                                    {
                                        "type": "TextBlock",
                                        "text": "${statusEmoji} ${model}",
                                        "weight": "bolder",
                                        "size": "large"
                                    }
                                ]
                            },
                            {
                                "type": "Column",
                                "width": "auto",
                                "items": [
                                    {
                                        "type": "TextBlock",
                                        "text": "${percentText}",
                                        "weight": "bolder",
                                        "size": "large",
                                        "color": "accent"
                                    }
                                ]
                            }
                        ]
                    },
                    {
                        "type": "TextBlock",
                        "text": "${planName} · ${cost} · ${context}",
                        "size": "small",
                        "isSubtle": true,
                        "spacing": "small"
                    },
                    {
                        "type": "TextBlock",
                        "text": "5h: ${percentText}  ${sevenDay}",
                        "size": "small",
                        "isSubtle": true,
                        "spacing": "none"
                    },
                    {
                        "type": "ColumnSet",
                        "columns": [
                            {
                                "type": "Column",
                                "width": "${percentValueClamped}",
                                "items": [
                                    {
                                        "type": "TextBlock",
                                        "text": " ",
                                        "size": "small"
                                    }
                                ],
                                "backgroundImage": {
                                    "url": "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPj/HwADBwIAMCbHYQAAAABJRU5ErkJggg==",
                                    "fillMode": "repeatHorizontally"
                                },
                                "minHeight": "8px"
                            },
                            {
                                "type": "Column",
                                "width": "stretch",
                                "items": []
                            }
                        ],
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
                                        "text": "Window: ${windowTokens} / ${tokenLimit}",
                                        "size": "small",
                                        "isSubtle": true
                                    }
                                ]
                            },
                            {
                                "type": "Column",
                                "width": "auto",
                                "items": [
                                    {
                                        "type": "TextBlock",
                                        "text": "${resetTime}",
                                        "size": "small",
                                        "isSubtle": true,
                                        "color": "attention"
                                    }
                                ]
                            }
                        ]
                    },
                    {
                        "type": "TextBlock",
                        "text": "Token Breakdown (All Time)",
                        "weight": "bolder",
                        "size": "small",
                        "spacing": "large"
                    },
                    {
                        "type": "FactSet",
                        "spacing": "small",
                        "facts": [
                            {
                                "title": "Total",
                                "value": "${totalTokens}"
                            },
                            {
                                "title": "Input",
                                "value": "${inputTokens}"
                            },
                            {
                                "title": "Output",
                                "value": "${outputTokens}"
                            },
                            {
                                "title": "Cache Write",
                                "value": "${cacheCreation}"
                            },
                            {
                                "title": "Cache Read",
                                "value": "${cacheRead}"
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
                        "type": "TextBlock",
                        "text": "${resetTime} · Updated ${updatedTime}",
                        "size": "small",
                        "isSubtle": true,
                        "spacing": "large"
                    }
                ]
            }
        ]
    }
    """;

    #endregion
}
