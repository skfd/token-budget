using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LlmTokenWidget.Providers.Zai;

public sealed class MessageParser
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public ParsedUsage ParseAllSessions()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var storagePath = Path.Combine(userProfile, ".local", "share", "opencode", "storage", "message");

        if (!Directory.Exists(storagePath))
            return new ParsedUsage();

        var result = new ParsedUsage();
        var sessions = new HashSet<string>();
        var messages = new List<ParsedMessage>();

        foreach (var sessionDir in Directory.EnumerateDirectories(storagePath))
        {
            var sessionName = Path.GetFileName(sessionDir);
            if (sessionName.StartsWith("ses_", StringComparison.OrdinalIgnoreCase))
            {
                sessions.Add(sessionName);
            }

            foreach (var messageFile in Directory.EnumerateFiles(sessionDir, "*.json"))
            {
                try
                {
                    var message = ParseMessageFile(messageFile);
                    if (message != null && message.Role == "assistant")
                    {
                        messages.Add(message);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to parse {messageFile}: {ex.Message}");
                }
            }
        }

        result.SessionCount = sessions.Count;
        result.MessageCount = messages.Count;

        long totalInput = 0, totalOutput = 0, totalReasoning = 0, totalCacheRead = 0, totalCacheWrite = 0;
        DateTimeOffset? earliest = null, latest = null;

        foreach (var msg in messages)
        {
            totalInput += msg.Tokens?.Input ?? 0;
            totalOutput += msg.Tokens?.Output ?? 0;
            totalReasoning += msg.Tokens?.Reasoning ?? 0;
            totalCacheRead += msg.Tokens?.Cache?.Read ?? 0;
            totalCacheWrite += msg.Tokens?.Cache?.Write ?? 0;

            if (msg.Time?.Created != null)
            {
                var created = DateTimeOffset.FromUnixTimeMilliseconds(msg.Time.Created);
                if (earliest == null || created < earliest) earliest = created;
                if (latest == null || created > latest) latest = created;
            }
        }

        result.TotalInput = totalInput;
        result.TotalOutput = totalOutput;
        result.TotalReasoning = totalReasoning;
        result.TotalCacheRead = totalCacheRead;
        result.TotalCacheWrite = totalCacheWrite;
        result.EarliestMessage = earliest;
        result.LatestMessage = latest;

        return result;
    }

    private static ParsedMessage? ParseMessageFile(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<ParsedMessage>(json, s_jsonOptions);
    }
}

public sealed class ParsedUsage
{
    public int SessionCount { get; set; }
    public int MessageCount { get; set; }
    public long TotalInput { get; set; }
    public long TotalOutput { get; set; }
    public long TotalReasoning { get; set; }
    public long TotalCacheRead { get; set; }
    public long TotalCacheWrite { get; set; }
    public DateTimeOffset? EarliestMessage { get; set; }
    public DateTimeOffset? LatestMessage { get; set; }

    public long TotalTokens => TotalInput + TotalOutput + TotalReasoning + TotalCacheRead + TotalCacheWrite;
}

public sealed class ParsedMessage
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("sessionID")]
    public string? SessionId { get; set; }

    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("time")]
    public ParsedTime? Time { get; set; }

    [JsonPropertyName("tokens")]
    public ParsedTokens? Tokens { get; set; }

    [JsonPropertyName("modelID")]
    public string? ModelId { get; set; }

    [JsonPropertyName("providerID")]
    public string? ProviderId { get; set; }
}

public sealed class ParsedTime
{
    [JsonPropertyName("created")]
    public long Created { get; set; }

    [JsonPropertyName("completed")]
    public long? Completed { get; set; }
}

public sealed class ParsedTokens
{
    [JsonPropertyName("total")]
    public long Total { get; set; }

    [JsonPropertyName("input")]
    public long Input { get; set; }

    [JsonPropertyName("output")]
    public long Output { get; set; }

    [JsonPropertyName("reasoning")]
    public long Reasoning { get; set; }

    [JsonPropertyName("cache")]
    public ParsedCache? Cache { get; set; }
}

public sealed class ParsedCache
{
    [JsonPropertyName("read")]
    public long Read { get; set; }

    [JsonPropertyName("write")]
    public long Write { get; set; }
}
