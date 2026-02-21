using LlmTokenWidget.Core;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace LlmTokenWidget.Providers.Qwen;

/// <summary>
/// Qwen local provider.
/// Parses JSONL session files and counts requests for rate limit estimation.
/// </summary>
public sealed class QwenLocalProvider : ILlmProvider, IDisposable
{
    private readonly JsonlParser _parser;
    private readonly UsageClient _usageClient;

    private FileSystemWatcher? _watcher;
    private bool _disposed;

    // Debounce FileSystemWatcher events
    private System.Threading.Timer? _debounceTimer;

    public string ProviderId => "qwen-local";
    public string DisplayName => "Alibaba Qwen";
    public TimeSpan PollingInterval => TimeSpan.FromSeconds(5);

    public event EventHandler? DataChanged;

    public QwenLocalProvider(HttpGateway gateway)
    {
        _parser = new JsonlParser();
        _usageClient = new UsageClient();

        StartWatching();
    }

    public Task<ProviderAvailability> CheckAvailabilityAsync()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var qwenDir = Path.Combine(userProfile, ".qwen", "projects");
        var exists = Directory.Exists(qwenDir);
        return Task.FromResult(new ProviderAvailability(
            exists,
            exists ? null : $"Directory not found: {qwenDir}"));
    }

    public async Task<UsageSnapshot> FetchUsageAsync(CancellationToken ct)
    {
        var usage = _parser.ParseAllSessions();

        var totalTokens = new TokenBreakdown(
            InputTokens: usage.TotalInput,
            OutputTokens: usage.TotalOutput,
            CacheCreationTokens: usage.TotalReasoning,
            CacheReadTokens: usage.TotalCacheRead);

        var oauthUsage = await _usageClient.FetchAsync(usage.MessageTimestamps, ct);

        var snapshot = new UsageSnapshot(
            TotalTokens: totalTokens,
            SessionCount: usage.SessionCount,
            MessageCount: usage.MessageCount,
            EarliestMessage: usage.EarliestMessage,
            LatestMessage: usage.LatestMessage,
            FetchedAt: DateTimeOffset.UtcNow,
            LiveStatus: null,
            OAuthUsage: oauthUsage);

        return snapshot;
    }

    private void StartWatching()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var projectsPath = Path.Combine(userProfile, ".qwen", "projects");

        if (!Directory.Exists(projectsPath))
            return;

        try
        {
            _watcher = new FileSystemWatcher(projectsPath)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.FileName
            };

            _watcher.Changed += OnFileChanged;
            _watcher.Created += OnFileChanged;
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"FileSystemWatcher failed: {ex.Message}");
        }
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        if (!e.FullPath.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase))
            return;

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
        _watcher?.Dispose();
        _debounceTimer?.Dispose();
    }
}
