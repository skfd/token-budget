using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Windows.Widgets.Providers;

namespace LlmTokenWidget.App;

[ComVisible(true)]
[ComDefaultInterface(typeof(IWidgetProvider))]
[Guid("9F910C81-08A4-461F-93A6-96809C70A95D")] // CRITICAL: Must match Program.cs and manifest
public sealed class WidgetProvider : IWidgetProvider, IWidgetProvider2
{
    private readonly Dictionary<string, object> _activeWidgets = new();

    public WidgetProvider()
    {
        System.Diagnostics.Debug.WriteLine("WidgetProvider constructor called");
        // Widget recovery will be handled by the Widgets Board calling Activate for each active widget
    }

    public void CreateWidget(WidgetContext widgetContext)
    {
        System.Diagnostics.Debug.WriteLine($"CreateWidget called for: {widgetContext.Id}");
        _activeWidgets[widgetContext.Id] = new object(); // Placeholder for Phase 2

        // Send initial static card
        UpdateWidget(widgetContext);
    }

    public void DeleteWidget(string widgetId, string customState)
    {
        _activeWidgets.Remove(widgetId);
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
        UpdateWidget(contextChangedArgs.WidgetContext);
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

    private void UpdateWidget(WidgetContext context)
    {
        try
        {
            var buildTime = DateTime.Now.ToString("HH:mm:ss");

            string templateJson = """
            {
                "type": "AdaptiveCard",
                "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
                "version": "1.5",
                "body": [
                    {
                        "type": "TextBlock",
                        "text": "Hello Widget!",
                        "size": "large",
                        "weight": "bolder"
                    },
                    {
                        "type": "TextBlock",
                        "text": "LLM Token Usage Widget - Phase 1 Scaffold",
                        "wrap": true
                    },
                    {
                        "type": "TextBlock",
                        "text": "Size: ${size}",
                        "size": "small",
                        "color": "accent"
                    },
                    {
                        "type": "TextBlock",
                        "text": "Deployed: ${deployTime}",
                        "size": "small",
                        "isSubtle": true
                    }
                ]
            }
            """;

            string dataJson = $$"""
            {
                "size": "{{context.Size}}",
                "deployTime": "{{buildTime}}"
            }
            """;

            var updateOptions = new WidgetUpdateRequestOptions(context.Id)
            {
                Template = templateJson,
                Data = dataJson
            };

            WidgetManager.GetDefault().UpdateWidget(updateOptions);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"UpdateWidget failed: {ex.Message}");
            throw;
        }
    }
}
