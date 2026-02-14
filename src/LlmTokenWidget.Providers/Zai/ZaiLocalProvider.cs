using LlmTokenWidget.Core;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace LlmTokenWidget.Providers.Zai;

public sealed class ZaiLocalProvider : ILlmProvider, IDisposable
{
    private readonly MessageParser _parser;
    private FileSystemWatcher? _storageWatcher;
    private Timer? _debounceTimer;
    private bool _disposed;

    public string ProviderId => "zai-local";
    public string DisplayName => "Z.ai";
    public TimeSpan PollingInterval => TimeSpan.FromSeconds(5);

    public event EventHandler? DataChanged;

    public ZaiLocalProvider()
    {
        _parser = new MessageParser();
        StartWatching();
    }

    public Task<ProviderAvailability> CheckAvailabilityAsync()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var storagePath = Path.Combine(userProfile, ".local", "share", "opencode", "storage", "message");
        var exists = Directory.Exists(storagePath);
        var hasCreds = CredentialReader.CredentialsExist();

        return Task.FromResult(new ProviderAvailability(
            exists,
            exists ? null : $"Opencode storage not found. Install opencode CLI to track Z.ai usage."));
    }

    public Task<UsageSnapshot> FetchUsageAsync(CancellationToken ct)
    {
        var usage = _parser.ParseAllSessions();

        var totalTokens = new TokenBreakdown(
            InputTokens: usage.TotalInput,
            OutputTokens: usage.TotalOutput,
            CacheCreationTokens: usage.TotalCacheWrite,
            CacheReadTokens: usage.TotalCacheRead);

        var snapshot = new UsageSnapshot(
            TotalTokens: totalTokens,
            SessionCount: usage.SessionCount,
            MessageCount: usage.MessageCount,
            EarliestMessage: usage.EarliestMessage,
            LatestMessage: usage.LatestMessage,
            FetchedAt: DateTimeOffset.UtcNow,
            LiveStatus: null,
            OAuthUsage: null);

        return Task.FromResult(snapshot);
    }

    private void StartWatching()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var storagePath = Path.Combine(userProfile, ".local", "share", "opencode", "storage", "message");

        if (!Directory.Exists(storagePath))
            return;

        try
        {
            _storageWatcher = new FileSystemWatcher(storagePath)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.FileName
            };

            _storageWatcher.Changed += OnFileChanged;
            _storageWatcher.Created += OnFileChanged;
            _storageWatcher.EnableRaisingEvents = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"FileSystemWatcher failed: {ex.Message}");
        }
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        if (!e.FullPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            return;

        _debounceTimer?.Dispose();
        _debounceTimer = new Timer(_ =>
        {
            DataChanged?.Invoke(this, EventArgs.Empty);
        }, null, 200, Timeout.Infinite);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _storageWatcher?.Dispose();
        _debounceTimer?.Dispose();
    }
}
