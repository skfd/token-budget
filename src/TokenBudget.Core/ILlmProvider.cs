namespace TokenBudget.Core;

/// <summary>
/// Contract for LLM usage data providers.
/// </summary>
public interface ILlmProvider
{
    /// <summary>Unique identifier for this provider (e.g., "claude-code-local").</summary>
    string ProviderId { get; }

    /// <summary>Human-readable name shown in widget UI.</summary>
    string DisplayName { get; }

    /// <summary>Check whether this provider has data available.</summary>
    Task<ProviderAvailability> CheckAvailabilityAsync();

    /// <summary>Fetch current token usage snapshot.</summary>
    Task<UsageSnapshot> FetchUsageAsync(CancellationToken ct);

    /// <summary>How often the widget should poll for updates.</summary>
    TimeSpan PollingInterval { get; }

    /// <summary>Raised when underlying data changes (e.g., new JSONL entries).</summary>
    event EventHandler? DataChanged;
}
