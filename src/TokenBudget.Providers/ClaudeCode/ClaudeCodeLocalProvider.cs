using TokenBudget.Core;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace TokenBudget.Providers.ClaudeCode;

/// <summary>
/// Claude Code local provider.
/// Reads real-time session data from the Statusline Bridge
/// and rate-limit data from Anthropic's OAuth usage API.
/// </summary>
public sealed class ClaudeCodeLocalProvider : ILlmProvider, IDisposable
{

    private readonly StatuslineReader _statusReader;
    private readonly TranscriptReader _transcriptReader = new();
    private readonly OAuthUsageClient _usageClient;

    private FileSystemWatcher? _statusWatcher;
    private bool _disposed;

    // Debounce FileSystemWatcher events
    private System.Threading.Timer? _debounceTimer;

    public string ProviderId => "claude-code-local";
    public string DisplayName => "Claude Code";
    public TimeSpan PollingInterval => TimeSpan.FromSeconds(5);

    public event EventHandler? DataChanged;

    public ClaudeCodeLocalProvider(HttpGateway gateway)
    {
        _statusReader = new StatuslineReader();
        _usageClient = new OAuthUsageClient(gateway);

        StartWatching();
    }

    public Task<ProviderAvailability> CheckAvailabilityAsync()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var claudeDir = Path.Combine(userProfile, ".claude");
        var exists = Directory.Exists(claudeDir);
        var hasCreds = OAuthCredentialReader.CredentialsExist();
        return Task.FromResult(new ProviderAvailability(
            exists,
            exists ? (hasCreds ? null : "OAuth credentials not found") : $"Directory not found: {claudeDir}"));
    }

    public async Task<UsageSnapshot> FetchUsageAsync(CancellationToken ct)
    {
        var liveStatus = ReadLiveSession();

        TokenBreakdown totalTokens = liveStatus is { TotalInputTokens: { } inTok, TotalOutputTokens: { } outTok }
            ? new TokenBreakdown(inTok, outTok, liveStatus.CacheCreationTokens ?? 0, liveStatus.CacheReadTokens ?? 0)
            : new TokenBreakdown(0, 0, 0, 0);

        // Rate limits come from two places: widget-data.json (written by the CLI
        // statusline only — the desktop app never writes it, and newer CLI builds
        // omit five_hour) and the OAuth usage API (cached 5 min). Per window, take
        // the fresher source and fall back to the other when it lacks that window.
        var localLimits = _statusReader.ReadRateLimits();
        var apiUsage = await _usageClient.FetchAsync(ct);
        var oauthUsage = MergeLimits(localLimits, apiUsage);

        var snapshot = new UsageSnapshot(
            TotalTokens: totalTokens,
            SessionCount: 0,
            MessageCount: 0,
            EarliestMessage: null,
            LatestMessage: liveStatus?.CapturedAt,
            FetchedAt: DateTimeOffset.UtcNow,
            LiveStatus: liveStatus,
            OAuthUsage: oauthUsage);

        return snapshot;
    }



    /// <summary>
    /// The current session comes from its transcript, which every surface writes.
    /// Cost and context % exist only in the CLI statusline's widget-data.json, so
    /// they are taken from it only when it describes that same session.
    /// </summary>
    private StatuslineData? ReadLiveSession()
    {
        var transcript = _transcriptReader.Read();
        var statusline = _statusReader.Read();
        if (transcript == null) return statusline;
        if (statusline == null || !string.Equals(statusline.SessionId, transcript.SessionId, StringComparison.OrdinalIgnoreCase))
            return transcript;

        return transcript with
        {
            CostUsd = statusline.CostUsd,
            ContextWindowUsedPercent = statusline.ContextWindowUsedPercent is > 0 ? statusline.ContextWindowUsedPercent : null,
            SessionTitle = transcript.SessionTitle ?? statusline.SessionTitle,
        };
    }

    internal static OAuthUsageData? MergeLimits(OAuthUsageData? local, OAuthUsageData? api)
    {
        if (local == null) return api;
        if (api == null) return local;

        var (fresh, stale) = local.FetchedAt > api.FetchedAt ? (local, api) : (api, local);
        // Everything beyond the two windows (credits, scoped limits, breakdown)
        // exists only in the API response, so start from it.
        return api with
        {
            FiveHour = fresh.FiveHour ?? stale.FiveHour,
            SevenDay = fresh.SevenDay ?? stale.SevenDay,
            FetchedAt = fresh.FetchedAt,
        };
    }

    private void StartWatching()
    {
        // Watch for statusline updates only
        var statusFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "widget-data.json");
        var statusDir = Path.GetDirectoryName(statusFile);
        if (Directory.Exists(statusDir))
        {
            try
            {
                _statusWatcher = new FileSystemWatcher(statusDir)
                {
                    Filter = "widget-data.json",
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime
                };
                
                _statusWatcher.Changed += OnFileChanged;
                _statusWatcher.Created += OnFileChanged;
                _statusWatcher.EnableRaisingEvents = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"FileSystemWatcher (Status) failed: {ex.Message}");
            }
        }
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        _debounceTimer?.Dispose();
        _debounceTimer = new System.Threading.Timer(_ =>
        {
            DataChanged?.Invoke(this, EventArgs.Empty);
        }, null, 200, Timeout.Infinite);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _statusWatcher?.Dispose();
        _debounceTimer?.Dispose();
    }
}
