# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Windows 11 Widgets Board widget for displaying LLM token usage, costs, and cooldown estimates. Primary focus is monitoring Claude Code subscription usage (Pro/Max plan rolling 5-hour token budget) by parsing local JSONL files, with extensibility for Anthropic API, OpenAI, and Google Gemini providers.

**Full architecture plan**: `C:\Users\kk\.claude\plans\joyful-marinating-thunder.md`

## Technology Stack

- **Platform**: Windows 11 Widgets Board
- **Runtime**: .NET 8 (`net8.0-windows10.0.19041.0`)
- **Framework**: Windows App SDK 1.6+
- **Packaging**: MSIX via Windows Application Packaging Project
- **UI**: Adaptive Cards (JSON-based declarative UI)
- **Architecture**: COM out-of-process server implementing IWidgetProvider/IWidgetProvider2

## Solution Structure

```
LlmTokenWidget.sln
├── src/LlmTokenWidget.Core/         # Interfaces, models, shared services
├── src/LlmTokenWidget.Providers/    # ClaudeCode/Anthropic/OpenAI/Gemini providers
├── src/LlmTokenWidget.App/          # COM widget provider executable
└── packaging/LlmTokenWidget.Package/ # MSIX packaging project
```

## Build Commands

### Build the solution
```powershell
dotnet build LlmTokenWidget.sln
```

### Build in Release mode
```powershell
dotnet build LlmTokenWidget.sln -c Release
```

### Run tests
```powershell
dotnet test
```

### Run a specific test
```powershell
dotnet test --filter "FullyQualifiedName~TestMethodName"
```

### Deploy to Windows 11 (requires Developer Mode enabled)
Build the packaging project in Visual Studio or:
```powershell
msbuild packaging/LlmTokenWidget.Package/LlmTokenWidget.Package.wapproj /p:Configuration=Release /p:Platform=x64
```

Then deploy the resulting MSIX from Visual Studio or via `Add-AppxPackage`.

## Critical Architecture Concepts

### GUID Synchronization

**CRITICAL**: One CLSID must match in exactly 3 places, or the widget will fail to activate:

1. `[Guid("...")]` attribute on `WidgetProvider` class
2. `CLSID_WidgetProvider` constant in `Program.cs`
3. `<com:Class Id="...">` and `<CreateInstance ClassId="...">` in `Package.appxmanifest`

Widget Definition IDs (`Claude_Usage_Widget`, `LLM_Summary_Widget`) must also match between manifest and `_widgetFactories` dictionary in `WidgetProvider.cs`.

### Provider Pattern

All data sources implement `ILlmProvider`:

```csharp
public interface ILlmProvider
{
    string ProviderId { get; }
    string DisplayName { get; }
    Task<ProviderAvailability> CheckAvailabilityAsync();
    Task<IUsageData> FetchUsageAsync(CancellationToken ct);
    Task<CooldownEstimate?> EstimateCooldownAsync(CancellationToken ct);
    TimeSpan PollingInterval { get; }
    bool RequiresApiKey { get; }
    string? CredentialTarget { get; }
}
```

Four implementations:
- **ClaudeCodeLocalProvider**: Zero-config, parses `~/.claude/projects/` JSONL files
- **AnthropicApiProvider**: Uses Admin API for organization-level usage
- **OpenAiApiProvider**: Uses Admin API for organization-level usage
- **GeminiApiProvider**: Stub (no historical usage API available)

### JSONL Parsing (Claude Code Local Data)

**File locations**:
- Main sessions: `%USERPROFILE%\.claude\projects\<project>\<session>.jsonl`
- Subagents: `%USERPROFILE%\.claude\projects\<project>\<session>\subagents\agent-*.jsonl`

**Format** (each line is a JSON object):
```json
{
  "type": "assistant",
  "timestamp": "2026-02-10T08:18:13.336Z",
  "message": {
    "model": "claude-sonnet-4-5-20250929",
    "usage": {
      "input_tokens": 7,
      "cache_creation_input_tokens": 234,
      "cache_read_input_tokens": 26589,
      "output_tokens": 1
    }
  }
}
```

**Parser requirements**:
- Filter for `type == "assistant"` entries only
- Sum all `message.usage.*` fields (input, output, cache_creation, cache_read)
- Include subagent files in totals
- Parse ISO 8601 timestamps for cooldown estimation

### Cooldown Estimation

**Algorithm**:
1. Collect all assistant messages from JSONL files with `timestamp` in `[now - 5h, now]`
2. Sum total tokens in rolling window
3. Compare against plan limits:
   - Pro: ~45M tokens / 5h
   - Max5: ~135M tokens / 5h
   - Max20: ~540M tokens / 5h
4. If over limit, calculate "time until reset" by finding when earliest entries exit the window
5. Display as percentage bar + status (green/yellow/red)

### Credential Storage

API keys stored in `Windows.Security.Credentials.PasswordVault`:
- Encrypted at rest, scoped to current user
- MSIX identity sandbox isolation
- Targets: `LlmTokenWidget:Anthropic`, `LlmTokenWidget:OpenAI`, `LlmTokenWidget:Gemini`

## Implementation Phases

1. **Scaffold + Hello Widget**: COM boilerplate, manifest registration, static Adaptive Card
2. **Claude Code Local Provider**: JSONL parser, cooldown estimator, FileSystemWatcher
3. **Settings + Credential Store**: Plan selector, API key input via customization card
4. **API Providers**: Anthropic/OpenAI Admin API integration
5. **Polish**: Performance optimization, error handling, theme support, assets

## Widget Definitions

Two widgets registered in `Package.appxmanifest`:

| Widget ID | Display Name | Sizes | Purpose |
|-----------|-------------|-------|---------|
| `Claude_Usage_Widget` | Claude Code Usage | S/M/L | Local JSONL token usage + cooldown |
| `LLM_Summary_Widget` | LLM Usage Summary | M/L | Multi-provider aggregated view |

## Polling Strategy

| Provider | Interval | Update Mechanism |
|----------|----------|------------------|
| Claude Code Local | 30s | FileSystemWatcher + timer fallback |
| Anthropic API | 5 min | Timer |
| OpenAI API | 5 min | Timer |
| Gemini API | 10 min | Timer |

Polling only runs when widgets are active (Widgets board open).

## Verification Steps

1. **Build**: Solution compiles without errors, all tests pass
2. **Deploy**: MSIX deploys via Visual Studio to local machine
3. **Discovery**: Win+W shows widgets in picker
4. **Add**: Widget can be added to Widgets board
5. **Data**: Widget displays real token counts matching `/cost` output in Claude Code
6. **Settings**: Customization panel allows plan selection and API key storage
7. **Updates**: Widget refreshes when new JSONL entries are added
8. **Sizing**: Widget renders correctly in Small/Medium/Large sizes

## Prerequisites

- Windows 11 with Developer Mode enabled
- Visual Studio 2022 with "Windows application development" workload
- .NET 8 SDK
- Windows App SDK 1.6+ NuGet package

## Key Files Reference

- `src/LlmTokenWidget.App/WidgetProvider.cs` — COM widget lifecycle (IWidgetProvider)
- `src/LlmTokenWidget.App/Program.cs` — COM server entry point with CLSID registration
- `src/LlmTokenWidget.Providers/ClaudeCode/JsonlParser.cs` — Critical path for local data
- `src/LlmTokenWidget.Providers/ClaudeCode/CooldownEstimator.cs` — Rolling window calculation
- `packaging/LlmTokenWidget.Package/Package.appxmanifest` — COM + widget registration
