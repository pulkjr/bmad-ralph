using System.Text.RegularExpressions;

namespace RalphLoop.Data.FileStore;

/// <summary>
/// Handles individual BMAD story .md file operations for file-based storage mode:
/// YAML front-matter ralph-story-id, title extraction, short description snippet,
/// Status: line updates, and Acceptance Criteria section replacement.
/// All methods are static — no instance state required.
/// </summary>
public static class StoryFileManager
{
    private const int ShortDescriptionMaxLength = 200;

    // ─── ralph-story-id front-matter ─────────────────────────────────────────

    /// <summary>
    /// Reads the <c>ralph-story-id</c> value from the YAML front-matter block.
    /// Returns null if the file has no front-matter or the key is absent.
    /// </summary>
    public static async Task<long?> ReadRalphStoryIdAsync(string filePath)
    {
        var lines = await File.ReadAllLinesAsync(filePath);
        if (lines.Length == 0 || lines[0].Trim() != "---")
            return null;

        for (int i = 1; i < lines.Length; i++)
        {
            if (lines[i].Trim() == "---")
                break;

            var kv = ParseFrontMatterKv(lines[i]);
            if (
                kv.HasValue
                && string.Equals(kv.Value.Key, "ralph-story-id", StringComparison.OrdinalIgnoreCase)
            )
            {
                if (long.TryParse(kv.Value.Value, out var id))
                    return id;
                return null;
            }
        }
        return null;
    }

    /// <summary>
    /// Writes (or updates) the <c>ralph-story-id</c> key in the YAML front-matter.
    /// If the file already has a front-matter block, the key is inserted/updated inside it.
    /// If no front-matter exists, a new block is prepended.
    /// </summary>
    public static async Task WriteRalphStoryIdAsync(string filePath, long id)
    {
        var lines = (await File.ReadAllLinesAsync(filePath)).ToList();
        var idLine = $"ralph-story-id: {id}";

        if (lines.Count > 0 && lines[0].Trim() == "---")
        {
            // Find closing ---
            int closingIdx = -1;
            for (int i = 1; i < lines.Count; i++)
            {
                if (lines[i].Trim() == "---")
                {
                    closingIdx = i;
                    break;
                }
            }

            if (closingIdx >= 0)
            {
                // Look for existing ralph-story-id inside the block
                bool found = false;
                for (int i = 1; i < closingIdx; i++)
                {
                    var kv = ParseFrontMatterKv(lines[i]);
                    if (
                        kv.HasValue
                        && string.Equals(
                            kv.Value.Key,
                            "ralph-story-id",
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        lines[i] = idLine;
                        found = true;
                        break;
                    }
                }

                if (!found)
                    lines.Insert(closingIdx, idLine);
            }
            else
            {
                // Unclosed front-matter — insert before the opening ---
                lines.InsertRange(0, ["---", idLine, "---"]);
            }
        }
        else
        {
            // No front-matter — prepend a new block
            lines.InsertRange(0, ["---", idLine, "---"]);
        }

        await File.WriteAllLinesAsync(filePath, lines);
    }

    // ─── Content extraction ───────────────────────────────────────────────────

    /// <summary>
    /// Reads the story title from the first H1 Markdown heading (<c># Title</c>).
    /// Returns an empty string if no H1 is found.
    /// </summary>
    public static async Task<string> ReadTitleAsync(string filePath)
    {
        await foreach (var line in ReadLinesAsync(filePath))
        {
            if (line.StartsWith("# ", StringComparison.Ordinal))
                return line[2..].Trim();
        }
        return string.Empty;
    }

    /// <summary>
    /// Reads the first ≤200 characters of meaningful content from the <c>## Story</c> section.
    /// Falls back to the first non-empty, non-heading paragraph if <c>## Story</c> is absent.
    /// Used to populate <c>story.Description</c> in SQLite for party-mode review context.
    /// </summary>
    public static async Task<string> ReadShortDescriptionAsync(string filePath)
    {
        var lines = await File.ReadAllLinesAsync(filePath);
        bool inFrontMatter = lines.Length > 0 && lines[0].Trim() == "---";
        bool frontMatterClosed = !inFrontMatter;
        bool inStorySection = false;
        var builder = new System.Text.StringBuilder();

        foreach (var line in lines)
        {
            // Skip YAML front-matter
            if (!frontMatterClosed)
            {
                if (line.Trim() == "---" && builder.Length == 0 && inFrontMatter)
                {
                    // still in front-matter; check if this is the closing ---
                    // We count open/close by tracking whether we passed the first ---
                    // Simple approach: skip until second ---
                }
                // Track front-matter open/close
                if (inFrontMatter && line.Trim() == "---" && builder.Length > 0)
                {
                    frontMatterClosed = true;
                    inFrontMatter = false;
                }
                else if (inFrontMatter)
                {
                    builder.Append('x'); // placeholder so we can track closing
                }
                continue;
            }

            var trimmed = line.Trim();

            if (trimmed.StartsWith("## Story", StringComparison.OrdinalIgnoreCase))
            {
                inStorySection = true;
                builder.Clear();
                continue;
            }

            if (inStorySection)
            {
                if (trimmed.StartsWith("## ", StringComparison.Ordinal))
                    break; // end of ## Story section

                if (!string.IsNullOrWhiteSpace(trimmed) && !trimmed.StartsWith('#'))
                {
                    if (builder.Length > 0)
                        builder.Append(' ');
                    builder.Append(trimmed);

                    if (builder.Length >= ShortDescriptionMaxLength)
                        break;
                }
            }
        }

        if (builder.Length == 0)
        {
            // Fallback: first non-empty non-heading paragraph in the file
            bool pastFrontMatter = !inFrontMatter;
            bool inFm = lines.Length > 0 && lines[0].Trim() == "---";
            bool fmClosed = !inFm;
            int fmLines = 0;
            foreach (var line in lines)
            {
                if (!fmClosed)
                {
                    fmLines++;
                    if (fmLines > 1 && line.Trim() == "---")
                        fmClosed = true;
                    continue;
                }
                var t = line.Trim();
                if (!string.IsNullOrWhiteSpace(t) && !t.StartsWith('#'))
                {
                    builder.Append(t);
                    if (builder.Length >= ShortDescriptionMaxLength)
                        break;
                    builder.Append(' ');
                }
            }
        }

        var result = builder.ToString().Trim();
        return result.Length > ShortDescriptionMaxLength
            ? result[..ShortDescriptionMaxLength]
            : result;
    }

    // ─── In-place file mutations ──────────────────────────────────────────────

    /// <summary>
    /// Updates the first <c>Status:</c> line in the file (outside front-matter) to <paramref name="newStatus"/>.
    /// If no <c>Status:</c> line is found, the file is left unchanged.
    /// </summary>
    public static async Task UpdateStatusLineAsync(string filePath, string newStatus)
    {
        var lines = (await File.ReadAllLinesAsync(filePath)).ToList();
        bool inFrontMatter = lines.Count > 0 && lines[0].Trim() == "---";
        int frontMatterClose = -1;

        if (inFrontMatter)
        {
            for (int i = 1; i < lines.Count; i++)
            {
                if (lines[i].Trim() == "---")
                {
                    frontMatterClose = i;
                    break;
                }
            }
        }

        int startFrom = frontMatterClose >= 0 ? frontMatterClose + 1 : (inFrontMatter ? 1 : 0);
        var statusRegex = new Regex(@"^(\s*Status:\s*)(.+)$", RegexOptions.IgnoreCase);

        for (int i = startFrom; i < lines.Count; i++)
        {
            var m = statusRegex.Match(lines[i]);
            if (m.Success)
            {
                lines[i] = $"{m.Groups[1].Value}{newStatus}";
                await File.WriteAllLinesAsync(filePath, lines);
                return;
            }
        }
        // No Status: line found — leave file unchanged
    }

    /// <summary>
    /// Replaces the content of the <c>## Acceptance Criteria</c> section with <paramref name="newAcMarkdown"/>.
    /// The section spans from the heading line to (but not including) the next <c>## </c> heading.
    /// If the section is not found, the file is left unchanged.
    /// </summary>
    public static async Task UpdateAcceptanceCriteriaAsync(string filePath, string newAcMarkdown)
    {
        var lines = (await File.ReadAllLinesAsync(filePath)).ToList();

        int acStart = -1;
        int nextSectionStart = lines.Count;

        for (int i = 0; i < lines.Count; i++)
        {
            if (
                acStart < 0
                && lines[i]
                    .TrimStart()
                    .StartsWith("## Acceptance Criteria", StringComparison.OrdinalIgnoreCase)
            )
            {
                acStart = i;
                continue;
            }

            if (
                acStart >= 0
                && i > acStart
                && lines[i].TrimStart().StartsWith("## ", StringComparison.Ordinal)
            )
            {
                nextSectionStart = i;
                break;
            }
        }

        if (acStart < 0)
            return; // Section not found — leave file unchanged

        var newLines = lines
            .Take(acStart + 1) // keep the heading
            .Append(string.Empty) // blank line after heading
            .Concat(newAcMarkdown.Split('\n').Select(l => l.TrimEnd('\r')))
            .Append(string.Empty) // blank line before next section
            .Concat(lines.Skip(nextSectionStart))
            .ToList();

        await File.WriteAllLinesAsync(filePath, newLines);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static async IAsyncEnumerable<string> ReadLinesAsync(string filePath)
    {
        await using var stream = File.OpenRead(filePath);
        using var reader = new StreamReader(stream);
        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync();
            if (line is not null)
                yield return line;
        }
    }

    private static (string Key, string Value)? ParseFrontMatterKv(string line)
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
