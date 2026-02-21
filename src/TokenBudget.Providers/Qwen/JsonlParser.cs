using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LlmTokenWidget.Providers.Qwen;

/// <summary>
/// Parses Qwen Code JSONL session files at ~/.qwen/projects/*/chats/*.jsonl.
/// Each line is a JSON object; we filter for type == "assistant" and sum usageMetadata.
/// </summary>
public sealed class JsonlParser
{
    private readonly string _projectsPath;

    public JsonlParser()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _projectsPath = Path.Combine(userProfile, ".qwen", "projects");
    }

    public QwenParsedUsage ParseAllSessions()
    {
        if (!Directory.Exists(_projectsPath))
            return new QwenParsedUsage();

        var result = new QwenParsedUsage();
        var sessionIds = new HashSet<string>();

        foreach (var projectDir in Directory.EnumerateDirectories(_projectsPath))
        {
            var chatsDir = Path.Combine(projectDir, "chats");
            if (!Directory.Exists(chatsDir))
                continue;

            foreach (var jsonlFile in Directory.EnumerateFiles(chatsDir, "*.jsonl"))
            {
                // Session ID is the file name without extension
                var sessionId = Path.GetFileNameWithoutExtension(jsonlFile);
                sessionIds.Add(sessionId);

                try
                {
                    ParseJsonlFile(jsonlFile, result);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"QwenJsonlParser: Failed to parse {jsonlFile}: {ex.Message}");
                }
            }
        }

        result.SessionCount = sessionIds.Count;
        return result;
    }

    private static void ParseJsonlFile(string path, QwenParsedUsage result)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;

                if (!root.TryGetProperty("type", out var typeProp) ||
                    typeProp.GetString() != "assistant")
                    continue;

                result.MessageCount++;

                // Parse timestamp
                if (root.TryGetProperty("timestamp", out var tsProp))
                {
                    var tsStr = tsProp.GetString();
                    if (tsStr != null && DateTimeOffset.TryParse(tsStr, out var ts))
                    {
                        if (result.EarliestMessage == null || ts < result.EarliestMessage)
                            result.EarliestMessage = ts;
                        if (result.LatestMessage == null || ts > result.LatestMessage)
                            result.LatestMessage = ts;

                        result.MessageTimestamps.Add(ts);
                    }
                }

                // Parse usageMetadata
                if (root.TryGetProperty("usageMetadata", out var usage))
                {
                    if (usage.TryGetProperty("promptTokenCount", out var prompt))
                        result.TotalInput += prompt.GetInt64();
                    if (usage.TryGetProperty("candidatesTokenCount", out var candidates))
                        result.TotalOutput += candidates.GetInt64();
                    if (usage.TryGetProperty("cachedContentTokenCount", out var cached))
                        result.TotalCacheRead += cached.GetInt64();
                    if (usage.TryGetProperty("thoughtsTokenCount", out var thoughts))
                        result.TotalReasoning += thoughts.GetInt64();
                }
            }
            catch (JsonException)
            {
                // Skip malformed lines
            }
        }
    }
}

public sealed class QwenParsedUsage
{
    public int SessionCount { get; set; }
    public int MessageCount { get; set; }
    public long TotalInput { get; set; }
    public long TotalOutput { get; set; }
    public long TotalCacheRead { get; set; }
    public long TotalReasoning { get; set; }
    public DateTimeOffset? EarliestMessage { get; set; }
    public DateTimeOffset? LatestMessage { get; set; }

    /// <summary>All assistant message timestamps, used for rolling-window request counting.</summary>
    public List<DateTimeOffset> MessageTimestamps { get; } = new();

    public long TotalTokens => TotalInput + TotalOutput + TotalCacheRead + TotalReasoning;
}
