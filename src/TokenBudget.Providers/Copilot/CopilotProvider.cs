using TokenBudget.Core;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace TokenBudget.Providers.Copilot;

/// <summary>
/// GitHub Copilot provider. API-only — no local data files.
/// Tracks premium request usage against monthly quota.
/// </summary>
public sealed class CopilotProvider : ILlmProvider, IDisposable
{
    private readonly CopilotUsageClient _usageClient;

    public string ProviderId => "copilot";
    public string DisplayName => "GitHub Copilot";
    public TimeSpan PollingInterval => TimeSpan.FromSeconds(60);

#pragma warning disable CS0067 // API-only provider, no local file changes to watch
    public event EventHandler? DataChanged;
#pragma warning restore CS0067

    public CopilotProvider(HttpGateway gateway)
    {
        _usageClient = new CopilotUsageClient(gateway);
    }

    public Task<ProviderAvailability> CheckAvailabilityAsync()
    {
        var hasToken = CopilotCredentialReader.CredentialsExist();
        return Task.FromResult(new ProviderAvailability(
            hasToken,
            hasToken ? null : "No GitHub token found. Save PAT to ~/.config/token-budget/copilot.json"));
    }

    public async Task<UsageSnapshot> FetchUsageAsync(CancellationToken ct)
    {
        var usage = await _usageClient.FetchAsync(ct);

        return new UsageSnapshot(
            TotalTokens: new TokenBreakdown(0, 0, 0, 0),
            SessionCount: 0,
            MessageCount: 0,
            EarliestMessage: null,
            LatestMessage: null,
            FetchedAt: DateTimeOffset.UtcNow,
            LiveStatus: null,
            OAuthUsage: usage);
    }

    /// <summary>Total premium requests used (from last API fetch).</summary>
    public long LastTotalUsed => _usageClient.LastTotalUsed;

    /// <summary>Monthly premium request quota for the current plan (from last API fetch).</summary>
    public long QuotaLimit => _usageClient.QuotaLimit;

    public void Dispose()
    {
    }
}
