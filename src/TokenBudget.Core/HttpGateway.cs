using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace TokenBudget.Core;

public enum ApiEndpoint
{
    AnthropicOAuthUsage,
    ZaiQuotaLimit,
    GitHubCopilotUsage
}

public sealed class HttpGateway : IDisposable
{
    private static readonly Dictionary<ApiEndpoint, string> Endpoints = new()
    {
        [ApiEndpoint.AnthropicOAuthUsage] = "https://api.anthropic.com/api/oauth/usage",
        [ApiEndpoint.ZaiQuotaLimit] = "https://api.z.ai/api/monitor/usage/quota/limit",
        [ApiEndpoint.GitHubCopilotUsage] = "https://api.github.com/copilot_internal/user",
    };

    private readonly HttpClient _http = new();

    public async Task<HttpResponseMessage> SendAsync(
        ApiEndpoint endpoint,
        string? bearerToken,
        CancellationToken ct,
        string? urlArg = null,
        IReadOnlyDictionary<string, string>? extraHeaders = null)
    {
        var url = Endpoints[endpoint];
        if (urlArg != null)
            url = string.Format(url, urlArg);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        if (!string.IsNullOrEmpty(bearerToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        if (extraHeaders != null)
        {
            foreach (var (key, value) in extraHeaders)
                request.Headers.TryAddWithoutValidation(key, value);
        }

        return await _http.SendAsync(request, ct);
    }

    public void Dispose() => _http.Dispose();
}
