# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Windows 11 Widgets Board widget for displaying LLM token usage, costs, and cooldown estimates. Monitors Claude Code subscription usage (Pro/Max plan rolling 5-hour token budget), Z.ai/GLM usage from opencode CLI, and GitHub Copilot premium request usage, with extensibility for other providers.

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
├── src/LlmTokenWidget.Providers/    # ClaudeCode/Zai/Copilot providers
│   ├── ClaudeCode/                  # Claude Code local provider
│   ├── Zai/                         # Z.ai/opencode CLI provider
│   └── Copilot/                     # GitHub Copilot API provider
├── src/LlmTokenWidget.App/          # COM widget provider executable
└── packaging/LlmTokenWidget.Package/ # MSIX packaging project
```

## Build Commands

### Quick rebuild and deploy (recommended)
```powershell
.\rebuild-deploy.ps1
```
This script handles the full cycle: version increment, process cleanup, rebuild, and package registration.

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

## When to Redeploy vs Rebuild

| What changed | Action needed |
|---|---|
| `Package.appxmanifest` (widget defs, COM registration, display names, sizes) | Full redeploy (`rebuild-deploy.ps1`) |
| MSIX package identity or capabilities | Full redeploy |
| Adding/removing projects from the solution | Full redeploy |
| C# code (providers, cards, widget logic) | Rebuild + kill `LlmTokenWidget.App.exe` (Widget Board relaunches it on demand) |
| External data files (JSONL, `widget-data.json`, `auth.json`) | Nothing — read at runtime |

**Why `rebuild-deploy.ps1` exists**: Windows aggressively caches widget metadata. Manifest changes (display name, widget IDs) won't appear until `WidgetService` and `WidgetBoard` processes are killed and the package is re-registered. The script handles all of this.

**Shortcut for code-only changes**: `dotnet build LlmTokenWidget.sln` then kill the running exe. No MSIX reinstall needed.

## Critical Architecture Concepts

### GUID Synchronization

**CRITICAL**: One CLSID must match in exactly 3 places, or the widget will fail to activate:

1. `[Guid("...")]` attribute on `WidgetProvider` class
2. `CLSID_WidgetProvider` constant in `Program.cs`
3. `<com:Class Id="...">` and `<CreateInstance ClassId="...">` in `Package.appxmanifest`

Widget Definition IDs (`Claude_Usage_Widget`, `Zai_Usage_Widget`) must also match between manifest and provider lookups in `WidgetProvider.cs`.

### Provider Pattern

All data sources implement `ILlmProvider`:

```csharp
public interface ILlmProvider
{
    string ProviderId { get; }
    string DisplayName { get; }
    Task<ProviderAvailability> CheckAvailabilityAsync();
    Task<UsageSnapshot> FetchUsageAsync(CancellationToken ct);
    TimeSpan PollingInterval { get; }
    event EventHandler? DataChanged;
}
```

Current implementations:
- **ClaudeCodeLocalProvider**: Reads `~/.claude/widget-data.json` for live session data + OAuth API for rate limits
- **ZaiLocalProvider**: Parses `~/.local/share/opencode/storage/message/` JSON files for token usage + Z.ai quota API for rate limits
- **CopilotProvider**: API-only provider fetching premium request usage from GitHub billing API (300/month Pro quota)

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

### Z.ai Provider (Opencode CLI Data)

**Data locations**:
- Messages: `%USERPROFILE%\.local\share\opencode\storage\message\<session>\*.json`
- Auth: `%USERPROFILE%\.local\share\opencode\auth.json`

**Message format** (each file is a JSON object):
```json
{
  "id": "msg_xxx",
  "sessionID": "ses_xxx",
  "role": "assistant",
  "time": {
    "created": 1739500000000,
    "completed": 1739500001000
  },
  "tokens": {
    "total": 1234,
    "input": 1000,
    "output": 200,
    "reasoning": 34,
    "cache": { "read": 500, "write": 100 }
  },
  "modelID": "glm-4",
  "providerID": "zai"
}
```

**Parser requirements**:
- Scan all session directories under `storage/message/`
- Filter for `role == "assistant"` messages only
- Sum `tokens.input`, `tokens.output`, `tokens.reasoning`, `tokens.cache.read`, `tokens.cache.write`
- Track session count and message count
- Convert millisecond timestamps to DateTimeOffset

### Z.ai Quota API

**Endpoint:** `GET https://api.z.ai/api/monitor/usage/quota/limit`

**Auth:** `Authorization: Bearer <API_KEY>` (from `~/.local/share/opencode/auth.json`, field `zai-coding-plan.key` or `zai.key`)

**Response:**
```json
{
  "code": 200,
  "msg": "Operation successful",
  "data": {
    "limits": [
      {
        "type": "TOKENS_LIMIT",
        "unit": 3,              // 3=hours
        "number": 5,            // 5-hour rolling window
        "percentage": 57,       // usage %
        "nextResetTime": 1771066083865  // reset timestamp (ms)
      },
      {
        "type": "TOKENS_LIMIT",
        "unit": 6,              // 6=weeks
        "number": 1,            // weekly quota
        "percentage": 15,
        "nextResetTime": 1771560256998
      },
      {
        "type": "TIME_LIMIT",
        "unit": 5,              // 5=months
        "number": 1,
        "usage": 1000,
        "currentValue": 0,
        "remaining": 1000,
        "percentage": 0,
        "nextResetTime": 1773374656988,
        "usageDetails": [
          { "modelCode": "search-prime", "usage": 0 },
          { "modelCode": "web-reader", "usage": 0 },
          { "modelCode": "zread", "usage": 0 }
        ]
      }
    ],
    "level": "pro"
  },
  "success": true
}
```

**Unit enum (observed):** 3=hours, 5=months, 6=weeks

**Mapping to widget:**
- `TOKENS_LIMIT` with `unit=3` → 5-hour quota (maps to `OAuthUsageData.FiveHour`)
- `TOKENS_LIMIT` with `unit=6` → Weekly quota (maps to `OAuthUsageData.SevenDay`)
- `TIME_LIMIT` → Monthly web search/reader quota (not displayed yet)

### GitHub Copilot Provider (API-Only)

**Credential location**: `~/.config/llm-token-widget/copilot.json`
```json
{ "token": "ghp_..." }
```
The token needs `manage_billing:copilot` scope.

**API endpoints**:
- `GET https://api.github.com/user` — fetches authenticated username (cached per session)
- `GET https://api.github.com/users/{username}/settings/billing/premium_request/usage` — premium request usage

**Usage response** (usageItems array):
```json
{
  "usageItems": [
    { "date": "2026-02-01", "gross_quantity": 5, "sku": "COPILOT_PREMIUM_MODEL_X" }
  ]
}
```

**Mapping to widget**:
- Sum `gross_quantity` across all items = total premium requests used
- Quota: hardcoded 300 (Pro plan)
- Reset: 1st of next month at 00:00 UTC (computed, not from API)
- Polling: 60 seconds (API-only, no local files)

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
| `Claude_Usage_Widget` | Claude Code Usage v18 | S/M/L | Local token usage + OAuth rate limits |
| `Zai_Usage_Widget` | Z.ai Usage | S/M/L | GLM/z.ai token usage from opencode CLI |
| `Copilot_Usage_Widget` | GitHub Copilot Usage | S/M/L | GitHub Copilot premium request usage |

## Polling Strategy

| Provider | Interval | Update Mechanism |
|----------|----------|------------------|
| Claude Code Local | 5s | FileSystemWatcher + timer fallback |
| Z.ai Local | 5s | FileSystemWatcher + timer fallback |
| GitHub Copilot | 60s | Timer only (API-only, no local files) |

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
- `src/LlmTokenWidget.Providers/ClaudeCode/StatuslineReader.cs` — Reads live session data from widget-data.json
- `src/LlmTokenWidget.Providers/ClaudeCode/OAuthUsageClient.cs` — Fetches rate-limit data from Anthropic API
- `src/LlmTokenWidget.Providers/Zai/MessageParser.cs` — Parses opencode storage JSON files
- `src/LlmTokenWidget.Providers/Zai/ZaiLocalProvider.cs` — Z.ai provider implementation
- `src/LlmTokenWidget.Providers/Zai/ZaiQuotaClient.cs` — Fetches quota/rate-limit data from Z.ai API
- `src/LlmTokenWidget.Providers/Copilot/CopilotCredentialReader.cs` — Reads GitHub PAT from config file
- `src/LlmTokenWidget.Providers/Copilot/CopilotUsageClient.cs` — Fetches premium request usage from GitHub API
- `src/LlmTokenWidget.Providers/Copilot/CopilotProvider.cs` — GitHub Copilot provider implementation
- `packaging/LlmTokenWidget.Package/Package.appxmanifest` — COM + widget registration
- `rebuild-deploy.ps1` — Full rebuild and deploy script
