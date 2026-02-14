# Security & Authentication

How the LLM Token Widget handles credentials and data access for each provider.

## Provider Comparison

| | Claude Code | Z.ai | GitHub Copilot |
|---|---|---|---|
| **Credential source** | `~/.claude/.credentials.json` (auto) | `~/.local/share/opencode/auth.json` (auto) | `gh auth token` or manual config |
| **Manual setup needed** | No | No | Only if `gh` CLI not installed |
| **API endpoints called** | `api.anthropic.com/api/oauth/usage` | `api.z.ai/api/monitor/usage/quota/limit` | `api.github.com/user`, `api.github.com/users/{user}/settings/billing/premium_request/usage` |
| **Local files read** | `~/.claude/widget-data.json` | `~/.local/share/opencode/storage/message/**/*.json` | None |
| **Token refresh** | No (see limitations) | No | No |
| **Graceful degradation** | Shows disk-cached data with simulated resets | Widget shows "no data" | Widget shows "no data" |

## Claude Code

### Credentials

**Source:** `%USERPROFILE%\.claude\.credentials.json`

The widget reads the `claudeAiOauth.accessToken` field from this file. This file is created and managed by Claude Code itself — no manual setup is required.

### Data accessed

| What | Path / URL | Purpose |
|---|---|---|
| Live session data | `%USERPROFILE%\.claude\widget-data.json` | Current model, cost, token counts, context window usage |
| OAuth usage API | `GET https://api.anthropic.com/api/oauth/usage` | Rate-limit utilization (5-hour, 7-day windows) |
| Disk cache | `%USERPROFILE%\.claude\oauth-usage-cache.json` | Persisted last-known API response (written by widget) |

The API request includes headers `anthropic-beta: oauth-2025-04-20` and `User-Agent: claude-code/2.0.37`.

### Required permissions

None beyond what Claude Code already provisions. The OAuth token is created during normal Claude Code login.

### When auth fails

If the credentials file is missing or the token has expired, the widget falls back to its disk-cached usage data. Cached rate-limit percentages are adjusted by simulating resets (if a limit's reset time has passed, its utilization is zeroed out). The widget continues showing data this way until Claude Code is opened and refreshes the token.

### Limitations

- **No token refresh.** The widget cannot refresh expired OAuth tokens because it doesn't have the `client_id` (only Claude Code does). After a cold start or long idle period, data may be stale until Claude Code runs again.
- **`widget-data.json` staleness.** If the file hasn't been updated in 5 minutes, the widget treats it as absent (Claude Code not running).
- **API cache.** Successful API responses are cached for 30 seconds in memory and persisted to disk.

## Z.ai

### Credentials

**Source:** `%USERPROFILE%\.local\share\opencode\auth.json`

The widget reads the API key from the `zai-coding-plan.key` field, falling back to `zai.key`. This file is created by the opencode CLI during login — no manual setup is required.

### Data accessed

| What | Path / URL | Purpose |
|---|---|---|
| Message files | `%USERPROFILE%\.local\share\opencode\storage\message\<session>\*.json` | Token counts per assistant message (input, output, reasoning, cache) |
| Quota API | `GET https://api.z.ai/api/monitor/usage/quota/limit` | Rate-limit utilization (5-hour rolling, weekly) |

The API request uses `Authorization: Bearer <API_KEY>`.

### Required permissions

None beyond what the opencode CLI already provisions. The API key is created during normal opencode login.

### When auth fails

If the auth file is missing or the key is invalid, the quota API call silently fails and the widget shows local token data only (no rate-limit bars). Local message parsing works regardless of API credentials.

### Limitations

- **No token refresh.** If the API key is revoked or expires, the widget cannot re-authenticate. The user must re-login via the opencode CLI.
- **API cache.** Successful API responses are cached for 30 seconds.

## GitHub Copilot

### Credentials

**Priority order:**

1. **`gh` CLI** — runs `gh auth token` to read the token from the GitHub CLI's credential store. Cached for the lifetime of the widget process.
2. **Manual config file** — `%USERPROFILE%\.config\llm-token-widget\copilot.json` with `{"token": "ghp_..."}`.

### Data accessed

| What | Path / URL | Purpose |
|---|---|---|
| User identity | `GET https://api.github.com/user` | Fetches authenticated username (cached per session) |
| Billing API | `GET https://api.github.com/users/{user}/settings/billing/premium_request/usage` | Premium request usage this billing cycle |

Both requests use `Authorization: Bearer <token>`.

### Required permissions

The GitHub token needs the **`user`** scope (or `manage_billing:copilot`). If using `gh` CLI, ensure you've authenticated with sufficient scopes:

```sh
gh auth login --scopes user
```

### Manual setup (only if `gh` CLI is unavailable)

1. Create a GitHub Personal Access Token with `user` scope.
2. Save it:
   ```json
   // %USERPROFILE%\.config\llm-token-widget\copilot.json
   {"token": "ghp_your_token_here"}
   ```

### When auth fails

If no token is found (neither `gh` CLI nor config file), or the token lacks the required scope, the widget shows no data. There is no fallback cache — the widget remains empty until a valid token is available.

### Limitations

- **`gh` CLI token is cached once.** The token retrieved from `gh auth token` is cached for the process lifetime. If the user re-authenticates `gh` with a different account, the widget must be restarted to pick it up.
- **API cache.** Successful API responses are cached for 30 seconds.
- **Quota is hardcoded.** The Pro plan limit of 300 premium requests/month is hardcoded. Other plan tiers are not yet supported.

## Network Access Architecture

All outbound HTTP requests are funneled through a single class: `HttpGateway` (`src/LlmTokenWidget.Core/HttpGateway.cs`). No other code in the project creates `HttpClient` instances or sends HTTP requests directly.

The gateway maintains a hardcoded allowlist of exactly 4 endpoints:

| Endpoint | URL |
|---|---|
| `AnthropicOAuthUsage` | `https://api.anthropic.com/api/oauth/usage` |
| `ZaiQuotaLimit` | `https://api.z.ai/api/monitor/usage/quota/limit` |
| `GitHubUser` | `https://api.github.com/user` |
| `GitHubCopilotUsage` | `https://api.github.com/users/{user}/settings/billing/premium_request/usage` |

Adding a new endpoint requires adding to the `ApiEndpoint` enum and the URL dictionary in `HttpGateway.cs`. This makes it easy to audit all network access in a single file.

## General Security Notes

- **No telemetry.** The widget does not send any data to third parties. All API calls go directly to the respective provider (Anthropic, Z.ai, GitHub).
- **No credential storage.** The widget does not store or copy credentials. It reads them from existing tool configuration files at runtime. The one exception is the disk cache at `~/.claude/oauth-usage-cache.json`, which stores API *response* data (utilization percentages and reset times), not credentials.
- **MSIX sandbox.** When installed via MSIX, the widget runs under package identity with standard sandbox isolation.
- **Polling only when active.** API calls and file reads only occur while the Widgets Board is open and the widget is visible. No background polling.
- **Read-only access.** The widget never writes to provider credential files. The only file it writes is its own OAuth usage cache.
