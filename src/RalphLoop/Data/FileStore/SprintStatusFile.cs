using System.Text.RegularExpressions;

namespace RalphLoop.Data.FileStore;

/// <summary>
/// Reads and writes BMAD sprint-status.yaml.
/// Provides key classification (story/epic/retrospective), file path resolution,
/// and round-trip safe status updates that preserve comments and blank lines.
/// </summary>
public sealed class SprintStatusFile
{
    private readonly string _yamlFilePath;
    private readonly string _yamlDir;
    private readonly List<string> _rawLines;

    /// <summary>Absolute path to the directory where story files live.</summary>
    public string StoryLocationAbsolute { get; }

    /// <summary>Ordered list of classified entries from <c>development_status</c>.</summary>
    public IReadOnlyList<StatusEntry> Entries { get; }

    private SprintStatusFile(
        string yamlFilePath,
        string storyLocationAbsolute,
        List<StatusEntry> entries,
        List<string> rawLines
    )
    {
        _yamlFilePath = yamlFilePath;
        _yamlDir = Path.GetDirectoryName(yamlFilePath)!;
        StoryLocationAbsolute = storyLocationAbsolute;
        Entries = entries;
        _rawLines = rawLines;
    }

    /// <summary>Loads and parses the sprint-status.yaml file.</summary>
    public static async Task<SprintStatusFile> LoadAsync(string yamlFilePath)
    {
        if (!File.Exists(yamlFilePath))
            throw new FileNotFoundException(
                $"sprint-status.yaml not found at '{yamlFilePath}'.",
                yamlFilePath
            );

        var lines = await File.ReadAllLinesAsync(yamlFilePath);
        var rawLines = new List<string>(lines);
        var yamlDir = Path.GetDirectoryName(Path.GetFullPath(yamlFilePath))!;

        // Parse story_location
        var storyLocationAbsolute = yamlDir; // default: same dir as yaml
        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("story_location:", StringComparison.OrdinalIgnoreCase))
            {
                var raw = trimmed["story_location:".Length..].Trim().Trim('"', '\'');
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    var resolved = Path.IsPathRooted(raw) ? raw : Path.Combine(yamlDir, raw);
                    storyLocationAbsolute = Path.GetFullPath(resolved);
                }
                break;
            }
        }

        // Parse development_status block
        var entries = ParseDevelopmentStatus(lines);

        return new SprintStatusFile(
            Path.GetFullPath(yamlFilePath),
            storyLocationAbsolute,
            entries,
            rawLines
        );
    }

    /// <summary>Returns only story entries (excludes epics and retrospectives).</summary>
    public IReadOnlyList<StatusEntry> GetStoriesOnly() =>
        Entries.Where(e => e.EntryType == EntryType.Story).ToList();

    /// <summary>Returns only epic entries.</summary>
    public IReadOnlyList<StatusEntry> GetEpicsOnly() =>
        Entries.Where(e => e.EntryType == EntryType.Epic).ToList();

    /// <summary>
    /// Updates the status of a key in-place, preserving all comments and blank lines.
    /// </summary>
    public async Task UpdateStatusAsync(string key, string newStatus)
    {
        // Find the line containing this key in the development_status block
        // and replace its status value. Lines are in the form: "  key: status"
        var keyPattern = $@"^\s+{Regex.Escape(key)}\s*:\s*";
        for (int i = 0; i < _rawLines.Count; i++)
        {
            if (Regex.IsMatch(_rawLines[i], keyPattern))
            {
                // Preserve leading whitespace
                var leadingWs = _rawLines[i].Length - _rawLines[i].TrimStart().Length;
                var ws = _rawLines[i][..leadingWs];
                _rawLines[i] = $"{ws}{key}: {newStatus}";
                break;
            }
        }
        await File.WriteAllLinesAsync(_yamlFilePath, _rawLines);
    }

    /// <summary>
    /// Resolves the expected file path for a story key.
    /// Returns <c>{StoryLocationAbsolute}/{storyKey}.md</c>.
    /// If that path does not exist, falls back to a glob match (single result only).
    /// </summary>
    public string ResolveStoryFilePath(string storyKey)
    {
        var exactPath = Path.Combine(StoryLocationAbsolute, $"{storyKey}.md");
        if (File.Exists(exactPath))
            return exactPath;

        // Glob fallback
        if (Directory.Exists(StoryLocationAbsolute))
        {
            var matches = Directory.GetFiles(StoryLocationAbsolute, $"*{storyKey}*.md").ToList();
            if (matches.Count == 1)
                return Path.GetFullPath(matches[0]);
            if (matches.Count > 1)
                throw new InvalidOperationException(
                    $"Ambiguous story file for key '{storyKey}': {string.Join(", ", matches)}"
                );
        }

        // Return the canonical path even if the file does not exist yet (will be created)
        return exactPath;
    }

    /// <summary>Classifies a sprint-status.yaml key into its entry type.</summary>
    public static EntryType ClassifyKey(string key)
    {
        if (key.EndsWith("-retrospective", StringComparison.OrdinalIgnoreCase))
            return EntryType.Retrospective;
        if (Regex.IsMatch(key, @"^\d+-\d+-"))
            return EntryType.Story;
        if (key.StartsWith("epic-", StringComparison.OrdinalIgnoreCase))
            return EntryType.Epic;
        return EntryType.Unknown;
    }

    // ─── Private helpers ──────────────────────────────────────────────────────

    private static List<StatusEntry> ParseDevelopmentStatus(IEnumerable<string> lines)
    {
        var entries = new List<StatusEntry>();
        bool inDevStatus = false;
        int devStatusIndent = -1;

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#'))
                continue;

            if (!inDevStatus)
            {
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("development_status:", StringComparison.OrdinalIgnoreCase))
                {
                    inDevStatus = true;
                    devStatusIndent = line.Length - line.TrimStart().Length;
                }
                continue;
            }

            // Detect end of development_status block (a line at the same or lower indent level
            // that starts a new key, not a continuation)
            var lineIndent = line.Length - line.TrimStart().Length;
            if (lineIndent <= devStatusIndent && !string.IsNullOrWhiteSpace(line.TrimStart()))
                break;

            // Parse "  key: value" lines
            var kv = ParseKeyValue(line);
            if (kv is null)
                continue;

            var (key, status) = kv.Value;
            var entryType = ClassifyKey(key);
            if (entryType is EntryType.Unknown or EntryType.Retrospective)
                continue;

            entries.Add(new StatusEntry(key, status, entryType));
        }

        return entries;
    }

    private static (string Key, string Status)? ParseKeyValue(string line)
    {
        var colon = line.IndexOf(':', StringComparison.Ordinal);
        if (colon <= 0)
            return null;

        var key = line[..colon].Trim();
        var value = line[(colon + 1)..].Trim().Trim('"', '\'');
        if (string.IsNullOrEmpty(key))
            return null;

        return (key, value);
    }
}

/// <summary>An entry parsed from the development_status block of sprint-status.yaml.</summary>
public record StatusEntry(string Key, string Status, EntryType EntryType);

/// <summary>Classification of a sprint-status.yaml development_status key.</summary>
public enum EntryType
{
    Story,
    Epic,
    Retrospective,
    Unknown,
}
