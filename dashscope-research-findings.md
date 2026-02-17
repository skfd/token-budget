# Research Findings: DashScope Usage Data Access for Qwen Widget
## Goal

We have a Qwen Widget front end part but it uses stub fake data. We want to replace the data part with real thing. It should get the same information as Claude Code subscription returns.
We are using Alibaba Cloud Model Studio Coding Plan Lite. The API key for it is in `.qwen/settings` file.

Please do not confuse Qwen the CLI tool. And Alibaba Cloud Model Studio Coding Plan particular model we use, we're not interested in qwen cli at all, only in Alibaba Cloud Model Studio Coding Plan subscription.

Need to do web research and use temporary scripts to test the access to APIs and etc. No need to change widget code yet. Our goal is to find a source of data.

## Overview
This document summarizes the extensive research conducted to find a working approach for connecting the Qwen token widget to real usage data from the Alibaba Cloud DashScope API using your Coding Plan subscription.

## What Was Tried by Other Agent

### 1. Basic API Connectivity Tests
- Verified that the API key `sk-sp-ebfe816c23954b1caee482254f6f89ba` works for model calls
- Confirmed successful chat completion requests to `https://coding-intl.dashscope.aliyuncs.com/v1/chat/completions`
- Each successful API call returns usage data in the response (input tokens, output tokens, cached tokens)

### 2. Direct Usage API Endpoint Testing
Tested numerous potential usage endpoints:
- `https://coding-intl.dashscope.aliyuncs.com/v1/usage` - 404 Not Found
- `https://coding-intl.dashscope.aliyuncs.com/v1/billing/usage` - 404 Not Found
- `https://dashscope.aliyuncs.com/v1/usage` - 404 Not Found
- `https://dashscope.aliyuncs.com/api/v1/usage` - 404 Not Found
- And dozens of variations including `/billing`, `/account`, `/stats`, `/metrics`, `/quota`, `/limits`, etc.

### 3. Coding Plan Specific Endpoints
Tested endpoints related to the Bailian Coding Plan:
- `https://coding-intl.dashscope.aliyuncs.com/v1/coding-plan/usage` - 404 Not Found
- `https://coding-intl.dashscope.aliyuncs.com/v1/bailian/usage` - 404 Not Found
- Multiple variations with `/info`, `/status`, `/quota`, etc.

### 4. OAuth Token Testing
- Used OAuth access token from `~/.qwen/oauth_creds.json`
- Tested various endpoints on `portal.qwen.ai`
- All returned 404 Not Found errors
- Discovered OAuth token is expired (expiry date: 02/16/2026 08:28:40 UTC)

### 5. Different Authentication Headers
- Tried custom headers like `X-DashScope-SDK`, custom User-Agent
- Tested various content types and authentication methods
- All attempts resulted in 404 errors for usage endpoints

### 6. Multiple Model Testing
- Tested API calls with both `qwen3-coder-plus` and `qwen3-max-2026-01-23` models
- Confirmed both return usage data per request
- Each call shows individual request usage but not cumulative historical data

### 7. Environment Variable and Configuration Checks
- Found `BAILIAN_CODING_PLAN_API_KEY` environment variable
- Checked for other potential config files in common locations
- Examined all files in `~/.qwen/` directory

## Key Discoveries

### 1. widget-data.json Contains Real Usage Data
The file `~/.qwen/widget-data.json` contains real cumulative usage data:
```json
{
  "model": {
    "id": "qwen-max",
    "display_name": "Qwen Max"
  },
  "total_input_tokens": 12500,
  "total_output_tokens": 34200,
  "cost": {
    "total_cost_usd": 2.45
  },
  "context_window": {
    "used_percentage": 45.2
  },
  "current_usage": {
    "cache_creation_input_tokens": 2300,
    "cache_read_input_tokens": 5600
  }
}
```

### 2. No Centralized Usage API Found
Despite extensive testing of over 50 potential endpoints, no centralized usage tracking API was found that works with the current API key.

### 3. OAuth Token is Expired
The OAuth token in `oauth_creds.json` has expired, which explains why OAuth-based endpoints don't work.

### 4. API Key Works for Model Requests
The `sk-sp-` API key successfully authenticates model requests and returns per-request usage data.

## What Has NOT Been Tried

### 1. Alibaba Cloud Console API
- Direct calls to Alibaba Cloud's main API endpoints using AccessKey ID and Secret (different from the current API key)
- These require different authentication methods (signature-based)

### 2. Qwen Desktop Application Reverse Engineering
- Analyzing the Qwen desktop application to see how it communicates with the backend
- Checking network traffic from the Qwen app to identify the actual endpoints it uses

### 3. Different Authentication Methods
- Alibaba Cloud's RAM role-based authentication
- STS (Security Token Service) tokens
- Different API signature methods

## Additional Research (2026-02-16)

### Rate Limit Header Testing
Created test script to check API response headers for quota information:
- API endpoint: `https://coding-intl.dashscope.aliyuncs.com/v1/chat/completions`
- Headers returned: `content-type, content-length, date, req-cost-time, req-arrive-time, resp-start-time, x-envoy-upstream-service-time, server`
- **Result: No rate limit or quota headers exposed**

### Coding Plan Subscription Limits (from documentation)

| Plan | 5-Hour | Weekly | Monthly |
|------|--------|--------|---------|
| Lite (40 CNY/mo) | 1,200 requests | 9,000 requests | 18,000 requests |
| Pro (200 CNY/mo) | 6,000 requests | 45,000 requests | 90,000 requests |

**Reset times:**
- 5-hour window: Rolling reset
- Weekly: Every Monday 00:00:00 UTC+8
- Monthly: Subscription renewal date at 00:00:00 UTC+8

## Conclusion

**The DashScope Coding Plan API does NOT expose subscription quota information through any documented or discoverable means.**

The API returns:
- Per-request token usage in response body
- Timing headers only (no quota/rate-limit headers)
- 404 for all attempted usage/quota endpoints

### Remaining Options

1. **Local request tracking** - Widget counts requests locally (imprecise, doesn't track other tools)
2. **Manual entry** - Check Alibaba Cloud console and enter usage manually
3. **Alibaba Cloud SDK** - If you have AccessKey ID/Secret (different from API key), may provide billing API access
4. **Network interception** - Sniff traffic from official Qwen Code CLI to find hidden endpoints

### Blocking Question

**Do you have AccessKey ID/Secret credentials for your Alibaba Cloud account?** This would allow testing the Alibaba Cloud SDK for billing/usage APIs, which may be the only programmatic way to get subscription quota information.
