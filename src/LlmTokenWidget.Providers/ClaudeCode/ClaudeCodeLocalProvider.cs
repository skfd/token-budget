using LlmTokenWidget.Core;

namespace LlmTokenWidget.Providers.ClaudeCode;

/// <summary>
/// Claude Code local provider — reads JSONL session files from disk.
/// Zero-config: no API keys needed, just parses ~/.claude/projects/ files.
/// </summary>
public sealed class ClaudeCodeLocalProvider : ILlmProvider, IDisposable
{
    private readonly string _projectsDir;
    private readonly JsonlParser _parser;
    private readonly CooldownEstimator _estimator;
    private readonly DetectedPlan _detectedPlan;
    private readonly StatuslineReader _statusReader;
    private FileSystemWatcher? _watcher;
    private FileSystemWatcher? _statusWatcher;
    private List<TokenEntry>? _cachedEntries;
    private DateTimeOffset _lastFetch = DateTimeOffset.MinValue;
    private readonly object _lock = new();
    private bool _disposed;

    // Debounce FileSystemWatcher events (they fire many times per write)
    private System.Threading.Timer? _debounceTimer;
    private const int DebounceMilliseconds = 2000;

    public string ProviderId => "claude-code-local";
    public string DisplayName => "Claude Code";
    public TimeSpan PollingInterval => TimeSpan.FromSeconds(5); // Poll faster for live status

    public event EventHandler? DataChanged;

    /// <summary>The configured subscription plan.</summary>
    public DetectedPlan DetectedPlan => _detectedPlan;

    public ClaudeCodeLocalProvider()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _projectsDir = Path.Combine(userProfile, ".claude", "projects");
        _parser = new JsonlParser();
        _statusReader = new StatuslineReader();

        // Load plan from config file
        var config = new WidgetConfig();
        _detectedPlan = config.Load();
        _estimator = new CooldownEstimator(_detectedPlan.TokenLimit);

        System.Diagnostics.Debug.WriteLine($"WidgetConfig: {_detectedPlan.Tier} ({_detectedPlan.Reason})");

        StartWatching();
    }

    public Task<ProviderAvailability> CheckAvailabilityAsync()
    {
        var exists = Directory.Exists(_projectsDir);
        return Task.FromResult(new ProviderAvailability(
            exists,
            exists ? null : $"Directory not found: {_projectsDir}"));
    }

    public Task<UsageSnapshot> FetchUsageAsync(CancellationToken ct)
    {
        // 1. Try reading live status from statusline bridge
        var liveStatus = _statusReader.Read();

        // 2. Get historical/cumulative data from JSONL
        var entries = GetEntries(ct);

        TokenBreakdown totalTokens;

        if (liveStatus != null && liveStatus.TotalInputTokens.HasValue && liveStatus.TotalOutputTokens.HasValue)
        {
            // Use live session totals if available
            // Note: Statusline gives totals but not separate cache breakdown in the 'total_' fields
            // We can try to use current_usage for cache if we want, but for now let's trust the totals
            totalTokens = new TokenBreakdown(
                liveStatus.TotalInputTokens.Value,
                liveStatus.TotalOutputTokens.Value,
                liveStatus.CacheCreationTokens ?? 0, 
                liveStatus.CacheReadTokens ?? 0);
        }
        else
        {
            // Fallback to summing JSONL files
            var totalInput = entries.Sum(e => e.Tokens.InputTokens);
            var totalOutput = entries.Sum(e => e.Tokens.OutputTokens);
            var totalCacheCreation = entries.Sum(e => e.Tokens.CacheCreationTokens);
            var totalCacheRead = entries.Sum(e => e.Tokens.CacheReadTokens);
            totalTokens = new TokenBreakdown(totalInput, totalOutput, totalCacheCreation, totalCacheRead);
        }

        // Count unique sessions by looking at the file paths (uuid groups)
        var sessionCount = entries
            .Select(e => e.Uuid)
            .Where(u => !string.IsNullOrEmpty(u))
            .Distinct()
            .Count();

        var snapshot = new UsageSnapshot(
            TotalTokens: totalTokens,
            SessionCount: sessionCount > 0 ? sessionCount : 1,
            MessageCount: entries.Count,
            EarliestMessage: entries.Count > 0 ? entries.Min(e => e.Timestamp) : null,
            LatestMessage: entries.Count > 0 ? entries.Max(e => e.Timestamp) : null,
            FetchedAt: DateTimeOffset.UtcNow,
            LiveStatus: liveStatus);

        return Task.FromResult(snapshot);
    }

    public Task<CooldownEstimate?> EstimateCooldownAsync(CancellationToken ct)
    {
        var entries = GetEntries(ct);
        if (entries.Count == 0)
            return Task.FromResult<CooldownEstimate?>(null);

        var estimate = _estimator.Estimate(entries);
        return Task.FromResult<CooldownEstimate?>(estimate);
    }

    private List<TokenEntry> GetEntries(CancellationToken ct)
    {
        lock (_lock)
        {
            // Re-parse if cache is stale (> 10 seconds old) or empty
            if (_cachedEntries == null || (DateTimeOffset.UtcNow - _lastFetch).TotalSeconds > 10)
            {
                _cachedEntries = _parser.ParseAll(_projectsDir, ct: ct);
                _lastFetch = DateTimeOffset.UtcNow;
            }
            return _cachedEntries;
        }
    }

    private void InvalidateCache()
    {
        lock (_lock)
        {
            _cachedEntries = null;
        }
    }

    private void StartWatching()
    {
        if (Directory.Exists(_projectsDir))
        {
            try
            {
                _watcher = new FileSystemWatcher(_projectsDir)
                {
                    Filter = "*.jsonl",
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.FileName
                };

                _watcher.Changed += OnFileChanged;
                _watcher.Created += OnFileChanged;
                _watcher.EnableRaisingEvents = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"FileSystemWatcher (JSONL) failed: {ex.Message}");
            }
        }

        // Watch for statusline updates
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
        // Debounce: reset the timer each time an event fires
        _debounceTimer?.Dispose();
        
        // Use shorter debounce for statusline updates (200ms) vs JSONL (2000ms)
        var delay = e.Name == "widget-data.json" ? 200 : DebounceMilliseconds;

        _debounceTimer = new System.Threading.Timer(_ =>
        {
            if (e.Name != "widget-data.json") InvalidateCache(); // Only invalidate JSONL cache for JSONL changes
            DataChanged?.Invoke(this, EventArgs.Empty);
        }, null, delay, Timeout.Infinite);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _watcher?.Dispose();
        _statusWatcher?.Dispose();
        _debounceTimer?.Dispose();
    }
}
