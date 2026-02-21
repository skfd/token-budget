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
        // Read live status from statusline bridge
        var liveStatus = _statusReader.Read();

        TokenBreakdown totalTokens;

        if (liveStatus != null && liveStatus.TotalInputTokens.HasValue && liveStatus.TotalOutputTokens.HasValue)
        {
            totalTokens = new TokenBreakdown(
                liveStatus.TotalInputTokens.Value,
                liveStatus.TotalOutputTokens.Value,
                liveStatus.CacheCreationTokens ?? 0, 
                liveStatus.CacheReadTokens ?? 0);
        }
        else
        {
            totalTokens = new TokenBreakdown(0, 0, 0, 0);
        }

        // Fetch rate-limit data from OAuth API
        var oauthUsage = await _usageClient.FetchAsync(ct);

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
