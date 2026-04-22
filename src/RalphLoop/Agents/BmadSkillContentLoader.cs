using System.Text.RegularExpressions;
using RalphLoop.Config;

namespace RalphLoop.Agents;

/// <summary>
/// Loads BMAD SKILL.md files and normalizes relative path references so they
/// remain valid when instructions are injected into agent prompts.
/// </summary>
public sealed class BmadSkillContentLoader(RalphLoopConfig config)
{
    /// <summary>
    /// Maps canonical skill IDs (used throughout the codebase) to their BMAD 6.x
    /// on-disk directory names. When a skill is not found under its canonical name,
    /// the alias is tried as a fallback so both pre-6.x and 6.x installations work.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> SkillAliases = new Dictionary<
        string,
        string
    >(StringComparer.OrdinalIgnoreCase)
    {
        ["bmad-dev"] = "bmad-agent-dev",
        ["bmad-architect"] = "bmad-agent-architect",
        ["bmad-pm"] = "bmad-agent-pm",
        ["bmad-tech-writer"] = "bmad-agent-tech-writer",
        ["bmad-ux-designer"] = "bmad-agent-ux-designer",
        ["bmad-quick-dev"] = "bmad-agent-quick-flow-solo-dev",
    };

    private readonly List<string> _skillDirs = SkillDirectoryResolver.Resolve(config);

    public string LoadForPrompt(string skillId)
    {
        var skillFile = ResolveSkillFile(skillId);
        var raw = File.ReadAllText(skillFile);
        var normalized = NormalizeRelativePaths(raw, Path.GetDirectoryName(skillFile)!);

        return $"""
            BMAD SKILL CONTEXT ({skillId}) FROM: {skillFile}
            Treat the following skill content as authoritative persona/workflow instructions:

            <bmad-skill>
            {normalized}
            </bmad-skill>
            """;
    }

    public bool TryLoadForPrompt(string skillId, out string content)
    {
        try
        {
            content = LoadForPrompt(skillId);
            return true;
        }
        catch
        {
            content = string.Empty;
            return false;
        }
    }

    private string ResolveSkillFile(string skillId)
    {
        var candidates = new List<string> { skillId };
        if (SkillAliases.TryGetValue(skillId, out var alias))
            candidates.Add(alias);

        foreach (var dir in _skillDirs)
        {
            foreach (var candidate in candidates)
            {
                var path = Path.Combine(dir, candidate, "SKILL.md");
                if (File.Exists(path))
                    return path;
            }
        }

        throw new InvalidOperationException(
            $"Required BMAD skill file not found for '{skillId}'. Expected SKILL.md under one of: {string.Join(", ", _skillDirs)}"
        );
    }

    private static string NormalizeRelativePaths(string input, string skillDir)
    {
        var output = input;

        // Markdown links/images: (...), where target is ./ or ../
        output = Regex.Replace(
            output,
            @"\((\.\.?/[^)\s]+)\)",
            m => $"({ToAbsolute(skillDir, m.Groups[1].Value)})"
        );

        // Backticked relative paths: `./foo/bar`
        output = Regex.Replace(
            output,
            @"`(\.\.?/[^`]+)`",
            m => $"`{ToAbsolute(skillDir, m.Groups[1].Value)}`"
        );

        // Quoted relative paths: "./foo" or '../foo'
        output = Regex.Replace(
            output,
            "\"(\\.?\\.?/[^\"\\s]+)\"|'(\\.?\\.?/[^'\\s]+)'",
            m =>
            {
                var quote = m.Value[0];
                var path = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
                return $"{quote}{ToAbsolute(skillDir, path)}{quote}";
            }
        );

        return output;
    }

    private static string ToAbsolute(string skillDir, string relativePath) =>
        Path.GetFullPath(Path.Combine(skillDir, relativePath));
}
