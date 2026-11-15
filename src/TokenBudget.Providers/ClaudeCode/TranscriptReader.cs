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
/// Parsing is incremental: only bytes appended since the last read are parsed.
/// </summary>
public sealed class TranscriptReader
{
    public static readonly TimeSpan ActiveWindow = TimeSpan.FromMinutes(30);

    private readonly string _projectsDir;

    private string? _path;
    private long _offset;
    private readonly Dictionary<string, Usage> _byMessageId = new();
    private Usage? _last;
    private string? _model;
    private string? _title;
    private string? _sessionId;

    private record struct Usage(long Input, long Output, long CacheWrite, long CacheRead);

    public TranscriptReader(string? projectsDir = null)
    {
        _projectsDir = projectsDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");
    }

    private readonly object _gate = new();

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

            if (!string.Equals(newest.FullName, _path, StringComparison.OrdinalIgnoreCase) || newest.Length < _offset)
                Reset(newest.FullName);

            ReadAppended();

            if (_last == null) return null;

            long input = 0, output = 0;
            foreach (var u in _byMessageId.Values)
            {
                input += u.Input;
                output += u.Output;
            }

            return new StatuslineData(
                ModelName: _model != null ? FriendlyModelName(_model) : null,
                CostUsd: null,
                ContextWindowUsedPercent: null,
                TotalInputTokens: input,
                TotalOutputTokens: output,
                // Cache figures describe the latest request, not a running sum:
                // every turn re-reads the whole cached prompt, so summing is meaningless.
                CacheCreationTokens: _last.Value.CacheWrite,
                CacheReadTokens: _last.Value.CacheRead,
                CapturedAt: new DateTimeOffset(newest.LastWriteTimeUtc, TimeSpan.Zero),
                SessionTitle: _title,
                SessionId: _sessionId);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"TranscriptReader: {ex.Message}");
            return null;
        }
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

    private void Reset(string path)
    {
        _path = path;
        _offset = 0;
        _byMessageId.Clear();
        _last = null;
        _model = null;
        _title = null;
        _sessionId = Path.GetFileNameWithoutExtension(path);
    }

    private void ReadAppended()
    {
        using var fs = new FileStream(_path!, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (fs.Length <= _offset) return;
        fs.Seek(_offset, SeekOrigin.Begin);

        var buffer = new byte[fs.Length - _offset];
        var read = 0;
        while (read < buffer.Length)
        {
            var n = fs.Read(buffer, read, buffer.Length - read);
            if (n == 0) break;
            read += n;
        }

        // Only consume complete lines; a half-written trailing line waits for next time.
        var lastNewline = Array.LastIndexOf(buffer, (byte)'\n', read - 1);
        if (lastNewline < 0) return;
        _offset += lastNewline + 1;

        var text = Encoding.UTF8.GetString(buffer, 0, lastNewline);
        foreach (var line in text.Split('\n'))
            ParseLine(line);
    }

    private void ParseLine(string line)
    {
        if (line.Length < 2) return;
        // Cheap pre-filter: most lines are user turns, attachments and request blobs.
        var isAssistant = line.Contains("\"type\":\"assistant\"");
        var isTitle = line.Contains("\"type\":\"custom-title\"");
        if (!isAssistant && !isTitle) return;

        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;

            if (type == "custom-title")
            {
                if (root.TryGetProperty("customTitle", out var ct) && ct.ValueKind == JsonValueKind.String)
                    _title = ct.GetString();
                return;
            }

            if (type != "assistant") return;
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
                : $"line{_byMessageId.Count}";
            _byMessageId[id] = u;
            _last = u;

            if (msg.TryGetProperty("model", out var m) && m.ValueKind == JsonValueKind.String &&
                m.GetString() is { Length: > 0 } model && model != "<synthetic>")
                _model = model;
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
