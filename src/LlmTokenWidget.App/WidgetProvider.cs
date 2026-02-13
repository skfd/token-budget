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
            var cooldownTask = _provider.EstimateCooldownAsync(CancellationToken.None);
            // These are synchronous under the hood (file I/O wrapped in Task.FromResult)
            var usage = usageTask.GetAwaiter().GetResult();
            var cooldown = cooldownTask.GetAwaiter().GetResult();

            var templateJson = GetTemplate(state.Size);
            var dataJson = BuildDataJson(usage, cooldown, state.Size);

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

    private string BuildDataJson(UsageSnapshot usage, CooldownEstimate? cooldown, WidgetSize size)
    {
        var total = usage.TotalTokens;
        var statusEmoji = cooldown?.Status switch
        {
            CooldownStatus.Green => "🟢",
            CooldownStatus.Yellow => "🟡",
            CooldownStatus.Red => "🔴",
            _ => "⚪"
        };

        var percentText = cooldown != null
            ? $"{cooldown.PercentUsed * 100:F1}%"
            : "N/A";

        var resetText = cooldown?.TimeUntilReset != null
            ? $"{cooldown.TimeUntilReset.Value.Hours}h {cooldown.TimeUntilReset.Value.Minutes}m"
            : "";

        var updatedTime = DateTimeOffset.Now.ToString("HH:mm:ss");

        var planName = _provider.DetectedPlan.Tier.ToString();

        return $$"""
        {
            "statusEmoji": "{{statusEmoji}}",
            "percentText": "{{percentText}}",
            "percentValue": {{(cooldown != null ? (int)(cooldown.PercentUsed * 100) : 0)}},
            "percentValueClamped": {{(cooldown != null ? Math.Min((int)(cooldown.PercentUsed * 100), 100) : 0)}},
            "totalTokens": "{{FormatNumber(total.Total)}}",
            "inputTokens": "{{FormatNumber(total.InputTokens)}}",
            "outputTokens": "{{FormatNumber(total.OutputTokens)}}",
            "cacheCreation": "{{FormatNumber(total.CacheCreationTokens)}}",
            "cacheRead": "{{FormatNumber(total.CacheReadTokens)}}",
            "tokenLimit": "{{FormatNumber(cooldown?.TokenLimit ?? 0)}}",
            "windowTokens": "{{FormatNumber(cooldown?.TokensInWindow ?? 0)}}",
            "messageCount": "{{usage.MessageCount}}",
            "resetTime": "{{resetText}}",
            "updatedTime": "{{updatedTime}}",
            "planName": "{{planName}}",
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
                        "text": "${statusEmoji} Claude Code",
                        "weight": "bolder",
                        "size": "medium"
                    },
                    {
                        "type": "TextBlock",
                        "text": "${windowTokens} / ${tokenLimit} (${planName})",
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
                                        "text": "${statusEmoji} Claude Code Usage",
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
                        "text": "${planName} · 5h window: ${windowTokens} / ${tokenLimit}",
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
                        "text": "${messageCount} messages · Updated ${updatedTime}",
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
                                        "text": "${statusEmoji} Claude Code Usage",
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
                        "text": "${planName} Plan · Rolling 5-Hour Window",
                        "size": "small",
                        "isSubtle": true,
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
                        "text": "${messageCount} messages · Updated ${updatedTime}",
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
