using LlmTokenWidget.Core;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace LlmTokenWidget.Providers.ClaudeCode;

/// <summary>
/// Claude Code local provider.
/// PHASE 3 CLEANUP: Removed JSONL parsing/estimation logic.
/// Now only returns real-time session data from the Statusline Bridge.
/// </summary>
public sealed class ClaudeCodeLocalProvider : ILlmProvider, IDisposable
{

    private readonly StatuslineReader _statusReader;
    private FileSystemWatcher? _statusWatcher;
    private bool _disposed;

    // Debounce FileSystemWatcher events
    private System.Threading.Timer? _debounceTimer;

    public string ProviderId => "claude-code-local";
    public string DisplayName => "Claude Code";
    public TimeSpan PollingInterval => TimeSpan.FromSeconds(5);

    public event EventHandler? DataChanged;

// DetectedPlan removed (Phase 3 cleanup)

    public ClaudeCodeLocalProvider()
    {
        _statusReader = new StatuslineReader();

        // Plan detection removed (Phase 3 cleanup)

        StartWatching();
    }

    public Task<ProviderAvailability> CheckAvailabilityAsync()
    {
        // For now, we just check if the status file path implies Claude is installed/configured
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var claudeDir = Path.Combine(userProfile, ".claude");
        var exists = Directory.Exists(claudeDir);
        return Task.FromResult(new ProviderAvailability(
            exists,
            exists ? null : $"Directory not found: {claudeDir}"));
    }

    public Task<UsageSnapshot> FetchUsageAsync(CancellationToken ct)
    {
        // Only read live status from statusline bridge
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
            // No data available
            totalTokens = new TokenBreakdown(0, 0, 0, 0);
        }

        var snapshot = new UsageSnapshot(
            TotalTokens: totalTokens,
            SessionCount: 0, // Not tracked without JSONL
            MessageCount: 0, // Not tracked without JSONL
            EarliestMessage: null,
            LatestMessage: liveStatus?.CapturedAt,
            FetchedAt: DateTimeOffset.UtcNow,
            LiveStatus: liveStatus);

        return Task.FromResult(snapshot);
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
