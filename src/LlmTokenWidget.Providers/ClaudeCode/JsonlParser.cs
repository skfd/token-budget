using System.Text.Json;
using LlmTokenWidget.Core;

namespace LlmTokenWidget.Providers.ClaudeCode;

/// <summary>
/// Parses Claude Code JSONL session files to extract token usage data.
/// Files are located at: %USERPROFILE%\.claude\projects\{project}\{session}.jsonl
/// Subagent files at: %USERPROFILE%\.claude\projects\{project}\{session}\subagents\agent-*.jsonl
/// </summary>
public sealed class JsonlParser
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Parse all JSONL files under the Claude projects directory.
    /// </summary>
    /// <param name="projectsDir">Path to ~/.claude/projects/</param>
    /// <param name="since">Only include entries after this time (null = all time)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of token entries from assistant messages.</returns>
    public List<TokenEntry> ParseAll(string projectsDir, DateTimeOffset? since = null, CancellationToken ct = default)
    {
        var entries = new List<TokenEntry>();

        if (!Directory.Exists(projectsDir))
            return entries;

        // Find all .jsonl files recursively
        var jsonlFiles = Directory.EnumerateFiles(projectsDir, "*.jsonl", SearchOption.AllDirectories);

        foreach (var filePath in jsonlFiles)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var fileEntries = ParseFile(filePath, since, ct);
                entries.AddRange(fileEntries);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                // Skip files we can't read — they may be locked by Claude Code
                System.Diagnostics.Debug.WriteLine($"JsonlParser: Skipping {filePath}: {ex.Message}");
            }
        }

        return entries;
    }

    /// <summary>
    /// Parse a single JSONL file.
    /// </summary>
    public List<TokenEntry> ParseFile(string filePath, DateTimeOffset? since = null, CancellationToken ct = default)
    {
        var entries = new List<TokenEntry>();

        // Open with FileShare.ReadWrite so we don't block Claude Code from writing
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(line))
                continue;

            try
            {
                var entry = ParseLine(line, since);
                if (entry != null)
                    entries.Add(entry);
            }
            catch (JsonException)
            {
                // Skip malformed lines
            }
        }

        return entries;
    }

    private static TokenEntry? ParseLine(string line, DateTimeOffset? since)
    {
        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;

        // Only process assistant messages
        if (!root.TryGetProperty("type", out var typeProp) || typeProp.GetString() != "assistant")
            return null;

        // Extract timestamp
        if (!root.TryGetProperty("timestamp", out var timestampProp))
            return null;

        var timestampStr = timestampProp.GetString();
        if (string.IsNullOrEmpty(timestampStr) || !DateTimeOffset.TryParse(timestampStr, out var timestamp))
            return null;

        // Filter by time window
        if (since.HasValue && timestamp < since.Value)
            return null;

        // Extract uuid
        var uuid = root.TryGetProperty("uuid", out var uuidProp)
            ? uuidProp.GetString() ?? ""
            : "";

        // Extract message.usage
        if (!root.TryGetProperty("message", out var messageProp))
            return null;

        // Extract model
        var model = messageProp.TryGetProperty("model", out var modelProp)
            ? modelProp.GetString() ?? "unknown"
            : "unknown";

        if (!messageProp.TryGetProperty("usage", out var usageProp))
            return null;

        var inputTokens = GetLong(usageProp, "input_tokens");
        var outputTokens = GetLong(usageProp, "output_tokens");
        var cacheCreationTokens = GetLong(usageProp, "cache_creation_input_tokens");
        var cacheReadTokens = GetLong(usageProp, "cache_read_input_tokens");

        var tokens = new TokenBreakdown(inputTokens, outputTokens, cacheCreationTokens, cacheReadTokens);
        return new TokenEntry(timestamp, uuid, model, tokens);
    }

    private static long GetLong(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.Number)
            return prop.GetInt64();
        return 0;
    }
}
