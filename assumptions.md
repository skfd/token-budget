# LLM Provider Subscription Assumptions

This document describes the assumptions we make about various LLM provider subscriptions and what the correct/ideal shape of these subscriptions should be. This is a product-level analysis, not a code implementation discussion.

---

## Table of Contents

1. [Claude Code (Anthropic)](#claude-code-anthropic)
2. [Z.ai (GLM-4 via Opencode CLI)](#zai-glm-4-via-opencode-cli)
3. [GitHub Copilot](#github-copilot)
4. [Qwen Code](#qwen-code)
5. [Ideal Subscription Model](#ideal-subscription-model)

---

## Claude Code (Anthropic)

### Current Assumptions

**Subscription Tiers:**
- **Pro Plan**: $20/month
- **Max5 Plan**: $100/month (5x Pro capacity)
- **Max20 Plan**: $400/month (20x Pro capacity)

**Quota Structure:**
- **5-hour rolling window** (primary rate limit)
  - Pro: ~45M tokens per 5-hour rolling window
  - Max5: ~135M tokens per 5-hour rolling window
  - Max20: ~540M tokens per 5-hour rolling window
- **7-day rolling window** (secondary limit)
  - Additional throttling layer to prevent sustained high usage
- **Model-specific limits** (for Max plans)
  - Separate 7-day quotas for Opus vs Sonnet/Haiku
  - OAuth apps have separate 7-day limits
- **Extra Usage** (Max plans only)
  - Monthly overage billing beyond included quotas
  - Credits-based system with monthly cap
  - Opt-in feature

**Token Types:**
- **Input tokens**: User prompts and context
- **Output tokens**: Model responses
- **Cache creation tokens**: Building prompt cache (billed at standard rate)
- **Cache read tokens**: Reading from prompt cache (90% discount)

**Data Sources:**
- **Local JSONL files**: Session history in `~/.claude/projects/*/session.jsonl`
- **Statusline bridge**: Real-time session data in `~/.claude/widget-data.json`
- **OAuth Usage API**: Rate limit data from Anthropic's backend

**Reset Behavior:**
- Rolling windows: Oldest entries continuously exit the window
- Monthly extras: Reset on 1st of month at 00:00 UTC
- No explicit "cooldown complete" notification—user must wait for tokens to age out

### What's Missing/Unclear

1. **Exact token limits**: Official documentation doesn't publish precise 5-hour limits; we estimate from observed behavior
2. **Cache write costs**: Unclear if cache creation tokens count differently in quotas vs billing
3. **Subagent accounting**: Assumption that subagent tokens count toward parent session's quota (correct per implementation)
4. **Multi-session behavior**: If user runs multiple sessions concurrently, tokens sum across all sessions
5. **Quota sharing**: Unclear if quota is per-machine, per-account, or per-API-key (assumed per-account)

### Ideal Model

**What Claude Code subscription should provide:**
1. ✅ **Rolling time windows** for bursty workloads (good for code agents)
2. ✅ **Prompt caching** to reduce costs for iterative sessions
3. ⚠️ **Transparent quota visibility**: Should show remaining quota in real-time, not just "usage %"
4. ❌ **Better cooldown UX**: Should show exact reset time instead of percentage + guessing
5. ❌ **Grace period**: Allow brief overages with warning instead of hard cutoff
6. ❌ **Per-project quotas**: Allow subdividing monthly quota across projects to prevent one runaway session from consuming all credits

---

## Z.ai (GLM-4 via Opencode CLI)

### Current Assumptions

**Subscription Tiers:**
- **Pro Plan**: ~$10-20/month (pricing not publicly documented)
- **Free Tier**: Limited usage (exact limits unknown)

**Quota Structure:**
- **5-hour rolling window** (primary rate limit)
  - Token-based limit (exact number not published)
  - API returns `percentage` and `nextResetTime`
- **Weekly window** (secondary limit)
  - Prevents sustained high usage over longer periods
- **Monthly limits** (for web search/reader features)
  - TIME_LIMIT type in API response
  - Not token-based; counts usage of specific features

**Token Types:**
- **Input tokens**: User prompts
- **Output tokens**: Model responses
- **Reasoning tokens**: For models with reasoning capability (like o1)
- **Cache read**: Reading from prompt cache
- **Cache write**: Building prompt cache

**Data Sources:**
- **Local JSON files**: Message history in `~/.local/share/opencode/storage/message/*/`
- **Quota API**: `GET https://api.z.ai/api/monitor/usage/quota/limit` with Bearer token
- **Auth file**: `~/.local/share/opencode/auth.json` contains API key

**Reset Behavior:**
- Rolling windows (5-hour, weekly): Continuous aging-out
- Monthly web feature limits: Reset on 1st of month

### What's Missing/Unclear

1. **Exact token limits**: API returns percentage but not total quota capacity
2. **Free tier limits**: No documentation on free tier quotas
3. **Model variations**: Unclear if different models (GLM-4, GLM-4-Plus, etc.) have different quota costs
4. **Credential scope**: Unclear if API key is per-user, per-machine, or per-installation
5. **Web feature integration**: TIME_LIMIT for web search/reader is tracked but not displayed in widget

### Ideal Model

**What Z.ai subscription should provide:**
1. ✅ **Multi-window approach** (5-hour + weekly) balances short bursts with sustained use
2. ✅ **API-provided quota data** instead of relying on local parsing
3. ⚠️ **Absolute limits in API**: Should return total quota (e.g., "45M tokens") not just percentage
4. ❌ **Unified quota display**: Web feature limits and token limits should be consolidated
5. ❌ **Historical usage**: API should provide daily/weekly breakdown for usage trends
6. ❌ **Model-specific costs**: Transparency on which models cost more quota

---

## GitHub Copilot

### Current Assumptions

**Subscription Tiers:**
- **Individual/Pro**: $10/month
- **Business**: $19/user/month
- **Enterprise**: $39/user/month

**Quota Structure:**
- **Premium request quota** (Pro/Individual only):
  - 300 premium requests per month (calendar month)
  - Resets on 1st of month at 00:00 UTC
  - Premium requests use GPT-4 or Claude Opus for complex tasks
  - Standard requests (unlimited) use GPT-3.5/Codex
- **No token-based limits**: Quota is request-based, not token-based
- **No rolling windows**: Simple monthly calendar reset

**Request Types:**
- **Premium requests**: Complex multi-file edits, architecture questions, advanced debugging
- **Standard requests**: Code completion, inline suggestions, chat (basic)

**Data Sources:**
- **GitHub API only**: No local data files
- **Endpoint**: `GET /users/{username}/settings/billing/premium_request/usage`
- **Authentication**: GitHub PAT with `user` scope, or GitHub CLI (`gh auth token`)

**Reset Behavior:**
- Hard reset on 1st of each month at 00:00 UTC
- No rollover of unused quota
- No overage billing (requests beyond 300 are rejected or downgraded to standard)

### What's Missing/Unclear

1. **Premium request criteria**: Unclear what triggers a "premium" request vs standard
2. **Business/Enterprise quotas**: Unclear if higher tiers have different premium request limits
3. **Downgrade behavior**: What happens when premium quota exhausted? Silent downgrade to GPT-3.5 or error?
4. **Multi-device sync**: How quota is shared across devices/IDEs for same account
5. **Historical data**: API only shows current month; no access to previous months' usage

### Ideal Model

**What GitHub Copilot subscription should provide:**
1. ✅ **Simple monthly quota** is easy to understand
2. ✅ **Separate premium/standard tiers** allows unlimited basic usage
3. ⚠️ **Quota visibility**: Should show "premium requests remaining" in IDE, not just backend API
4. ❌ **Request type transparency**: Should clearly label when a request will use premium quota
5. ❌ **Quota sharing controls**: Allow team admins to allocate premium requests across team members
6. ❌ **Rollover or banking**: Allow unused premium requests to carry over (e.g., max 600 banked)
7. ❌ **Token-based alternative**: Option to purchase token quota instead of request quota for power users

---

## Qwen Code

### Current Assumptions

**Subscription Tiers:**
- **Coding Plan (Free)**: Access via Alibaba Cloud DashScope
- **Pay-as-you-go**: Unknown pricing structure

**Quota Structure:**
- **5-hour rolling window**: 1,200 requests
- **Weekly window**: 9,000 requests (resets Monday 00:00 UTC+8)
- **Monthly window**: 18,000 requests (resets 1st of month 00:00 UTC+8)
- **No token-based limits**: Quota is request-based (one assistant message = one request)
- **No API for quotas**: All rate limiting is done client-side by counting local requests

**Token Types (tracked but not limited):**
- **Prompt tokens**: User input
- **Candidate tokens**: Model output
- **Cached content tokens**: Cache reads
- **Thoughts tokens**: Reasoning tokens (for o1-style models)

**Data Sources:**
- **Local JSONL files only**: `~/.qwen/projects/*/chats/*.jsonl`
- **No backend API**: Client-side estimation by counting assistant messages in time windows
- **Timezone**: UTC+8 (China Standard Time) for weekly/monthly resets

**Reset Behavior:**
- 5-hour rolling: Continuous (oldest requests exit window)
- Weekly: Monday 00:00 CST
- Monthly: 1st of month 00:00 CST

### What's Missing/Unclear

1. **Official quota documentation**: No published limits; we estimate from observed client behavior
2. **Quota enforcement**: Unclear if limits are enforced server-side or client-side
3. **Token vs request**: Unclear why tokens are tracked if quota is request-based
4. **Commercial plans**: No information on paid tier quotas or pricing
5. **Multi-device sync**: How quota is shared/tracked across different machines
6. **Timezone handling**: Hardcoded CST may cause confusion for non-China users

### Ideal Model

**What Qwen Code subscription should provide:**
1. ⚠️ **Request-based quota** is simpler than tokens, but less fair (short vs long requests)
2. ❌ **Server-side quota API**: Should provide authoritative quota data instead of client-side guessing
3. ❌ **Token-based alternative**: Offer option to track by tokens instead of requests for fairness
4. ❌ **Localized reset times**: Allow users to set timezone for weekly/monthly resets
5. ❌ **Unified quota**: Consolidate 5-hour/weekly/monthly into clearer single limit
6. ❌ **API authentication**: Should require API key instead of relying on local file parsing

---

## Ideal Subscription Model

Based on the analysis above, here's what an ideal LLM provider subscription should look like:

### Core Principles

1. **Transparency**: Users should know exactly what they're paying for and what's remaining
2. **Flexibility**: Mix of short-term burst capacity and long-term sustained usage
3. **Predictability**: Clear reset schedules, no surprise throttling
4. **Fairness**: Quota accounting matches actual resource consumption

### Quota Structure

**Multi-Window Approach** (best of all providers):
- **Hourly burst**: Small limit (e.g., 10M tokens) for rapid iteration
- **Daily rolling**: Medium limit (e.g., 50M tokens) for sustained work
- **Monthly calendar**: Large limit (e.g., 500M tokens) for overall capacity
- **Annual rollover**: Allow banking unused quota (e.g., max 2 months)

**What to measure:**
- ✅ **Token-based** (fairer than request-based)
- ✅ **Model-weighted** (GPT-4/Opus cost more than 3.5/Haiku)
- ✅ **Cache-discounted** (cache reads cost 10% of writes)

### Data Visibility

**Real-time APIs:**
- `GET /quota/current` - Returns absolute remaining quotas (not percentages)
- `GET /quota/history` - Daily usage breakdown for past 30 days
- `GET /quota/forecast` - Predicted reset time based on current usage

**Local caching:**
- Local data files for offline usage tracking (like Claude Code JSONL)
- Sync with server on reconnect to handle multi-device usage

### Reset & Overage

**Graceful throttling:**
1. **Warnings at 80%, 90%, 95%** of quota
2. **Soft limit at 100%**: Allow 10% overage with throttled speed
3. **Hard limit at 110%**: Reject requests with clear error message
4. **Reset notification**: Proactive alert when quota refills

**Overage options:**
- **On-demand credits**: Purchase one-time token packs
- **Automatic top-up**: Opt-in to auto-purchase when quota exhausted (with monthly cap)
- **Rollover**: Unused quota carries to next period (max 2x base quota)

### Subscription Tiers

**Free Tier:**
- 100K tokens/day rolling
- No cache, no advanced models
- Good for trying out the service

**Pro Tier ($20/month):**
- 50M tokens/day rolling
- 500M tokens/month calendar
- Prompt caching enabled
- All models (GPT-4, Claude, etc.)

**Team Tier ($50/user/month):**
- 150M tokens/day rolling per user
- 1.5B tokens/month calendar per user
- Shared team quota pool (10% bonus)
- Admin dashboard for quota allocation

**Enterprise (Custom pricing):**
- Custom quota limits
- Dedicated capacity (no throttling)
- SLA guarantees
- Private deployment options

### Widget Integration

**What our widget should show:**
1. **Current usage**: Absolute numbers (e.g., "23M / 50M tokens")
2. **Multiple windows**: Show all active quotas (hourly, daily, monthly)
3. **Reset timers**: Exact countdown to next reset
4. **Trend analysis**: "You're using 150% of your daily average"
5. **Cost estimation**: "At this rate, you'll hit monthly limit in 12 days"
6. **Provider comparison**: Side-by-side quota status for all enabled providers

---

## Summary of Current vs Ideal

| Feature | Claude Code | Z.ai | Copilot | Qwen | Ideal Model |
|---------|-------------|------|---------|------|-------------|
| **Quota basis** | Tokens | Tokens | Requests | Requests | Tokens (model-weighted) |
| **Time windows** | 5h + 7d | 5h + weekly | Monthly | 5h + weekly + monthly | Hourly + daily + monthly |
| **Data source** | Local + API | Local + API | API only | Local only | Local + API (synced) |
| **Quota visibility** | Percentage | Percentage | Count | Client-side | Absolute + percentage |
| **Reset timing** | Rolling | Rolling + calendar | Calendar | Rolling + calendar | Mixed (rolling + calendar) |
| **Overage handling** | Hard stop | Hard stop | Downgrade | Unknown | Soft limit + warnings |
| **Multi-device** | Synced | Unclear | Synced | Per-device | Synced with fallback |
| **Historical data** | None | None | Current month only | None | 30-day history via API |
| **Transparency** | ⚠️ Medium | ⚠️ Medium | ✅ Good | ❌ Poor | ✅ Excellent |

---

## Design Recommendations

1. **Normalize quota display**: Convert all providers to consistent "X/Y tokens" or "X/Y requests" format
2. **Unified reset timers**: Show all reset times in user's local timezone
3. **Trend warnings**: Alert when usage is significantly above average
4. **Provider health**: Show API connectivity status, last successful sync
5. **Fallback modes**: When API unavailable, show cached data with staleness indicator
6. **Multi-provider strategy**: Help users distribute workload across providers to maximize available quota

---

## Subscription Tier Detection: Hardcoded vs Automatic

This section documents how the widget currently determines user subscription tiers for each provider.

### Current Implementation Status

| Provider | Tier Detection Method | Hardcoded Limits | Notes |
|----------|----------------------|------------------|-------|
| **Claude Code** | ❌ Not detected | None | OAuth API returns utilization % but not absolute limits or plan tier |
| **Z.ai** | ❌ Not detected | None | Quota API returns utilization % but not absolute limits or plan tier |
| **GitHub Copilot** | ⚠️ Hardcoded assumption | **300 requests/month** (Pro tier) | Hardcoded in `CopilotUsageClient.cs:17` |
| **Qwen Code** | ⚠️ Hardcoded assumption | **1,200 req/5h, 9,000/week, 18,000/month** (Coding Plan Lite) | Hardcoded in `UsageClient.cs:16-18` |

### Detailed Analysis

#### Claude Code (Anthropic)
- **Detection method**: None — widget displays raw utilization percentages from OAuth API
- **What the API provides**: Percentage-based rate limits (e.g., "57% of 5-hour quota used")
- **What it doesn't provide**: 
  - Absolute token limits (45M for Pro, 135M for Max5, etc.)
  - User's actual subscription tier
  - Total quota capacity
- **Implication**: Widget cannot show "23M / 45M tokens" — only "57% used"
- **Code location**: `src/LlmTokenWidget.Providers/ClaudeCode/OAuthUsageClient.cs`

**Why this works**:
- Utilization percentage is sufficient for the widget's purpose
- Exact limits aren't needed since the API already calculates usage %
- Different plan tiers (Pro/Max5/Max20) share the same percentage-based display

#### Z.ai (GLM-4)
- **Detection method**: None — widget displays raw utilization percentages from quota API
- **What the API provides**: Percentage-based quotas with reset timestamps
- **What it doesn't provide**:
  - Absolute token limits
  - User's subscription tier (Pro vs Free)
  - Total quota capacity in tokens
- **Implication**: Widget shows percentage without knowing if user has Pro or Free tier
- **Code location**: `src/LlmTokenWidget.Providers/Zai/ZaiQuotaClient.cs`

**Why this works**:
- API returns `percentage` field directly (e.g., 57%)
- No tier-specific behavior needed — same display for all tiers
- Percentage is universal across subscription levels

#### GitHub Copilot
- **Detection method**: ⚠️ **HARDCODED** — Assumes Pro tier (300 requests/month)
- **Hardcoded constant**: `ProQuotaLimit = 300` in `CopilotUsageClient.cs`
- **What the API provides**: 
  - Array of usage items with `grossQuantity` (total requests used)
  - No quota limit information
  - No subscription tier information
- **What it doesn't provide**:
  - User's actual tier (Individual/Business/Enterprise)
  - Tier-specific quota limits
- **Implication**: Widget will show incorrect data for Business or Enterprise users
- **Code location**: 
  - `src/LlmTokenWidget.Providers/Copilot/CopilotUsageClient.cs:17` (constant)
  - `src/LlmTokenWidget.Providers/Copilot/CopilotUsageClient.cs:82` (calculation)
  - `src/LlmTokenWidget.App/WidgetProvider.cs:535` (display: "300 premium requests")

**Known issue**:
- Business ($19/user/month) and Enterprise ($39/user/month) tiers may have different quotas
- Current implementation assumes all users are on Individual/Pro plan
- No GitHub API endpoint provides subscription tier or quota limits

#### Qwen Code
- **Detection method**: ⚠️ **HARDCODED** — Assumes Coding Plan Lite tier
- **Hardcoded constants** in `UsageClient.cs`:
  - `FiveHourLimit = 1200` requests
  - `WeeklyLimit = 9000` requests  
  - `MonthlyLimit = 18000` requests
- **What the implementation provides**: 
  - Client-side request counting by parsing local JSONL files
  - Rolling window calculations
- **What it doesn't provide**:
  - Server-authoritative quota data
  - User's actual subscription tier
  - Detection of commercial/paid plans
- **Implication**: Widget will show incorrect data for users on different/paid tiers
- **Code location**: `src/LlmTokenWidget.Providers/Qwen/UsageClient.cs:16-18`

**Known issue**:
- No DashScope API available for Coding Plan quotas
- All quota estimation is client-side based on assumed limits
- Paid tiers likely have different limits, but no way to detect them

### Problems with Current Approach

1. **GitHub Copilot**: Business/Enterprise users see incorrect quota (300 instead of their actual limit)
2. **Qwen Code**: Paid plan users see incorrect quotas (hardcoded Lite tier limits)
3. **No tier auto-detection**: Widget cannot adapt to user's actual subscription level
4. **No API support**: GitHub and Qwen don't expose tier/quota info via API

### Ideal Solution

For all providers, the widget should:

1. **Tier auto-detection via API**: Providers should return subscription tier in their API responses
2. **Absolute quota limits**: APIs should include total quota (not just utilization %)
3. **Fallback to percentage-only**: If tier unknown, show percentage without absolute values
4. **User configuration**: Allow manual tier selection in widget settings as fallback

**Example ideal API response** (Claude Code):
```json
{
  "subscription": {
    "tier": "max5",
    "display_name": "Max 5x"
  },
  "five_hour": {
    "limit": 135000000,
    "used": 76950000,
    "utilization": 57.0,
    "resets_at": "2026-02-18T23:30:00Z"
  }
}
```

### Recommendation for Widget

**Short-term** (current phase):
- ✅ Keep current hardcoded limits for Copilot and Qwen (document as known limitation)
- ✅ Display percentage-only for Claude Code and Z.ai (works for all tiers)
- ❌ Do not claim to support multiple tiers without detection

**Medium-term** (future enhancement):
- Add widget settings UI to manually select subscription tier
- Store tier preference per provider in local config
- Update quota calculations based on user-selected tier

**Long-term** (requires API changes):
- Advocate for providers to add tier/quota info to their APIs
- Implement automatic tier detection when APIs support it
- Remove hardcoded limits entirely

---

*Last updated: 2026-02-18*
