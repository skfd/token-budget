using LlmTokenWidget.Core;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace LlmTokenWidget.Providers.Qwen;

/// <summary>
/// Qwen local provider.
/// Provides placeholder usage data similar to Claude Code widget.
/// </summary>
public sealed class QwenLocalProvider : ILlmProvider, IDisposable
{
    private readonly StatuslineReader _statusReader;
    private readonly UsageClient _usageClient;

    private FileSystemWatcher? _statusWatcher;
    private bool _disposed;

    // Debounce FileSystemWatcher events
    private System.Threading.Timer? _debounceTimer;

    public string ProviderId => "qwen-local";
    public string DisplayName => "Qwen";
    public TimeSpan PollingInterval => TimeSpan.FromSeconds(5);

    public event EventHandler? DataChanged;

    public QwenLocalProvider(HttpGateway gateway)
    {
        _statusReader = new StatuslineReader();
        _usageClient = new UsageClient(gateway);

        StartWatching();
    }

    public Task<ProviderAvailability> CheckAvailabilityAsync()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var qwenDir = Path.Combine(userProfile, ".qwen");
        var exists = Directory.Exists(qwenDir);
        return Task.FromResult(new ProviderAvailability(
            exists,
            exists ? null : $"Directory not found: {qwenDir}"));
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

        // Fetch rate-limit data from API
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
        var statusFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".qwen", "widget-data.json");
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