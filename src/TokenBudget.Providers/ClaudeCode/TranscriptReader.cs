using System.Text;
using System.Text.Json;
using TokenBudget.Core;

namespace TokenBudget.Providers.ClaudeCode;

/// <summary>
/// Reads the current Claude Code session straight from its transcript
/// (~/.claude/projects/&lt;project&gt;/&lt;session&gt;.jsonl). Unlike widget-data.json,
/// which only the CLI statusline writes, every surface (CLI, desktop app, IDE)
/// writes these, so this works wherever Claude Code runs.
///
/// "Current" means the main-session transcript written to most recently, within
/// <see cref="ActiveWindow"/>. With several sessions running at once the widget
/// follows whichever answered last; the session title says which one it is.
///
/// Parsing is incremental and kept per transcript, so switching between parallel
/// sessions never re-reads a file from the start (some run past 100 MB).
/// </summary>
public sealed class TranscriptReader
{
    public static readonly TimeSpan ActiveWindow = TimeSpan.FromMinutes(30);

    private readonly string _projectsDir;
    private readonly Dictionary<string, Session> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    private record struct Usage(long Input, long Output, long CacheWrite, long CacheRead);

    private sealed class Session
    {
        public required string Path;
        public long Offset;
        public readonly Dictionary<string, Usage> ByMessageId = new();
        public Usage? Last;
        public string? Model;
        public string? Title;
        public string? Project;
        public DateTime LastSeenUtc;
    }

    private static readonly byte[] AssistantMarker = Encoding.UTF8.GetBytes("\"type\":\"assistant\"");
    private static readonly byte[] TitleMarker = Encoding.UTF8.GetBytes("\"type\":\"custom-title\"");

    public TranscriptReader(string? projectsDir = null)
    {
        _projectsDir = projectsDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");
    }

    public StatuslineData? Read()
    {
        lock (_gate) return ReadLocked();
    }

    private StatuslineData? ReadLocked()
    {
        try
        {
            var newest = FindNewestTranscript();
            if (newest == null || DateTime.UtcNow - newest.LastWriteTimeUtc > ActiveWindow)
                return null;

            if (!_sessions.TryGetValue(newest.FullName, out var s) || newest.Length < s.Offset)
                _sessions[newest.FullName] = s = new Session { Path = newest.FullName };
            s.LastSeenUtc = DateTime.UtcNow;
            Evict();

            ReadAppended(s);

            if (s.Last == null) return null;

            long input = 0, output = 0;
            foreach (var u in s.ByMessageId.Values)
            {
                input += u.Input;
                output += u.Output;
            }

            return new StatuslineData(
                ModelName: s.Model != null ? FriendlyModelName(s.Model) : null,
                CostUsd: null,
                ContextWindowUsedPercent: null,
                TotalInputTokens: input,
                TotalOutputTokens: output,
                // Cache figures describe the latest request, not a running sum:
                // every turn re-reads the whole cached prompt, so summing is meaningless.
                CacheCreationTokens: s.Last.Value.CacheWrite,
                CacheReadTokens: s.Last.Value.CacheRead,
                CapturedAt: new DateTimeOffset(newest.LastWriteTimeUtc, TimeSpan.Zero),
                // Untitled sessions (SDK, fresh CLI) fall back to the project folder.
                SessionTitle: s.Title ?? s.Project,
                SessionId: Path.GetFileNameWithoutExtension(s.Path));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"TranscriptReader: {ex.Message}");
            return null;
        }
    }

    private void Evict()
    {
        var cutoff = DateTime.UtcNow - ActiveWindow;
        foreach (var key in _sessions.Where(kv => kv.Value.LastSeenUtc < cutoff).Select(kv => kv.Key).ToList())
            _sessions.Remove(key);
    }

    private FileInfo? FindNewestTranscript()
    {
        if (!Directory.Exists(_projectsDir)) return null;
        FileInfo? newest = null;
        foreach (var dir in Directory.EnumerateDirectories(_projectsDir))
        {
            // Top level only: subagent transcripts live in <session>/subagents/.
            foreach (var f in new DirectoryInfo(dir).EnumerateFiles("*.jsonl", SearchOption.TopDirectoryOnly))
            {
                if (newest == null || f.LastWriteTimeUtc > newest.LastWriteTimeUtc)
                    newest = f;
            }
        }
        return newest;
    }

    /// <summary>
    /// Streams the bytes appended since the last read in fixed chunks, parsing only
    /// complete lines that look relevant. A half-written trailing line waits for next time.
    /// </summary>
    private static void ReadAppended(Session s)
    {
        using var fs = new FileStream(s.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 1, FileOptions.SequentialScan);
        if (fs.Length <= s.Offset) return;
        fs.Seek(s.Offset, SeekOrigin.Begin);

        var buffer = new byte[1 << 20];
        var filled = 0;
        while (true)
        {
            if (filled == buffer.Length)
                Array.Resize(ref buffer, buffer.Length * 2); // a single line longer than the buffer
            var n = fs.Read(buffer, filled, buffer.Length - filled);
            if (n == 0) break;
            filled += n;

            var start = 0;
            int nl;
            while ((nl = Array.IndexOf(buffer, (byte)'\n', start, filled - start)) >= 0)
            {
                ParseLine(s, buffer.AsMemory(start, nl - start));
                start = nl + 1;
            }
            s.Offset += start;
            Buffer.BlockCopy(buffer, start, buffer, 0, filled - start);
            filled -= start;
        }
    }

    private static void ParseLine(Session s, ReadOnlyMemory<byte> line)
    {
        if (line.Length < 2) return;
        // Cheap pre-filter on raw bytes: most lines are user turns, attachments and request blobs.
        var span = line.Span;
        if (span.IndexOf(AssistantMarker) < 0 && span.IndexOf(TitleMarker) < 0) return;

        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            var type = root.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;

            if (type == "custom-title")
            {
                if (root.TryGetProperty("customTitle", out var ct) && ct.ValueKind == JsonValueKind.String)
                    s.Title = ct.GetString();
                return;
            }

            if (type != "assistant") return;
            if (root.TryGetProperty("cwd", out var cwd) && cwd.ValueKind == JsonValueKind.String &&
                cwd.GetString() is { Length: > 0 } dir)
                s.Project = Path.GetFileName(dir.TrimEnd('\\', '/'));
            if (root.TryGetProperty("isSidechain", out var sc) && sc.ValueKind == JsonValueKind.True) return;
            if (!root.TryGetProperty("message", out var msg) || !msg.TryGetProperty("usage", out var usage)) return;

            var u = new Usage(
                Long(usage, "input_tokens"),
                Long(usage, "output_tokens"),
                Long(usage, "cache_creation_input_tokens"),
                Long(usage, "cache_read_input_tokens"));

            // One API response is written as several lines (one per content block),
            // each repeating the same usage. Keep the last one per message id.
            var id = msg.TryGetProperty("id", out var mid) && mid.ValueKind == JsonValueKind.String
                ? mid.GetString()!
                : $"line{s.ByMessageId.Count}";
            s.ByMessageId[id] = u;
            s.Last = u;

            if (msg.TryGetProperty("model", out var m) && m.ValueKind == JsonValueKind.String &&
                m.GetString() is { Length: > 0 } model && model != "<synthetic>")
                s.Model = model;
        }
        catch (JsonException)
        {
            // Skip malformed lines.
        }
    }

    private static long Long(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;

    /// <summary>"claude-opus-5-5" → "Opus 5.5"; "claude-sonnet-4-5-20250929" → "Sonnet 4.5".</summary>
    public static string FriendlyModelName(string id)
    {
        var bare = id.Split('[')[0];
        if (bare.StartsWith("claude-", StringComparison.OrdinalIgnoreCase)) bare = bare[7..];
        var parts = bare.Split('-', StringSplitOptions.RemoveEmptyEntries);
        var words = new List<string>();
        var version = new List<string>();
        foreach (var p in parts)
        {
            if (p.Length >= 8 && p.All(char.IsDigit)) continue; // date suffix
            if (p.All(char.IsDigit)) version.Add(p);
            else words.Add(char.ToUpperInvariant(p[0]) + p[1..]);
        }
        var name = string.Join(" ", words);
        return version.Count > 0 ? $"{name} {string.Join(".", version)}".Trim() : (name.Length > 0 ? name : id);
    }
}
