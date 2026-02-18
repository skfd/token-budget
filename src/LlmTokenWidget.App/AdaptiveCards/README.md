# Adaptive Card Templates

This directory contains the Adaptive Card template definitions for the LLM Token Widget. Each template is a JSON file that defines the UI layout for different widget sizes and providers.

## Template Files

### Claude Code Provider
- `claude-small.json` - Small (1x1) widget layout
- `claude-medium.json` - Medium (2x2) widget layout with weekly limits
- `claude-large.json` - Large (4x2) widget layout with token breakdown

### Qwen Code Provider
- `qwen-small.json` - Small (1x1) widget layout
- `qwen-medium.json` - Medium (2x2) widget layout with quota breakdown
- `qwen-large.json` - Large (4x2) widget layout with detailed quota info

### Z.ai Provider
- `zai-small.json` - Small (1x1) widget layout
- `zai-medium.json` - Medium (2x2) widget layout
- `zai-large.json` - Large (4x2) widget layout with token breakdown

### GitHub Copilot Provider
- `copilot-small.json` - Small (1x1) widget layout
- `copilot-medium.json` - Medium (2x2) widget layout
- `copilot-large.json` - Large (4x2) widget layout

## Template Variables

All templates support data binding using the `${variableName}` syntax. Common variables include:

### Progress Bar Variables
- `${percentValueClamped}` - Width percentage for the filled portion (0-100)
- `${percentRemaining}` - Width percentage for the unfilled portion (0-100)
- `${percentText}` - Percentage text to display (e.g., "57%")
- `${barFillUrl}` - Data URI for the progress bar fill color
- `${trackUrl}` - Data URI for the progress bar track color
- `${statusColor}` - Text color based on usage level (green/amber/red)

### Time Variables
- `${resetTime}` - Human-readable reset time (e.g., "Resets in 2h 15m")
- `${sevenDayReset}` - Weekly quota reset time
- `${monthlyReset}` - Monthly quota reset time

### Usage Variables
- `${usageText}` - Usage summary (e.g., "150/300 premium requests")
- `${extraUsage}` - Additional usage information
- `${inputTokens}` - Token count for input
- `${outputTokens}` - Token count for output
- `${cacheCreation}` - Cache write tokens
- `${cacheRead}` - Cache read tokens

### Weekly Quota Variables (Claude/Qwen)
- `${sevenDayPercent}` - Weekly usage percentage
- `${sevenDayValueClamped}` - Weekly progress bar fill width
- `${sevenDayRemaining}` - Weekly progress bar unfilled width
- `${sevenDayBarFillUrl}` - Weekly progress bar fill color
- `${sevenDayStatusColor}` - Weekly status color

### Monthly Quota Variables (Qwen)
- `${monthlyPercent}` - Monthly usage percentage
- `${monthlyValueClamped}` - Monthly progress bar fill width
- `${monthlyRemaining}` - Monthly progress bar unfilled width

## Loading Templates

Templates are loaded at runtime as embedded resources by `AdaptiveCardTemplateLoader.cs`. The loader:

1. Reads the JSON file from the assembly's embedded resources
2. Caches the template for reuse
3. Returns the template string for data binding

## Modifying Templates

When modifying templates:

1. Edit the JSON file directly in this directory
2. Ensure the JSON is valid (use a JSON validator)
3. Test all widget sizes to ensure proper rendering
4. Rebuild the project to embed the updated template
5. Redeploy the MSIX package to apply changes

The templates use the [Adaptive Cards](https://adaptivecards.io/) schema version 1.5. Refer to the Adaptive Cards documentation for available UI elements and properties.

## Build Integration

The templates are automatically included as embedded resources via the project file:

```xml
<ItemGroup>
  <EmbeddedResource Include="AdaptiveCards\*.json" />
</ItemGroup>
```

The embedded resource name format is: `LlmTokenWidget.App.AdaptiveCards.{filename}`
