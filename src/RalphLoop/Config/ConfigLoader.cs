using System.Text.Json;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace RalphLoop.Config;

public static class ConfigLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static RalphLoopConfig Load(string projectPath)
    {
        var configPath = Path.Combine(projectPath, "ralph-loop.json");

        RalphLoopConfig config;
        if (File.Exists(configPath))
        {
            var json = File.ReadAllText(configPath);
            try
            {
                config =
                    JsonSerializer.Deserialize<RalphLoopConfig>(json, JsonOptions)
                    ?? new RalphLoopConfig();
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException(
                    $"Failed to parse ralph-loop.json: {ex.Message}",
                    ex
                );
            }
        }
        else
        {
            config = new RalphLoopConfig();
        }

        config.ProjectPath = Path.GetFullPath(projectPath);

        // Resolve skill directories
        config.SkillDirectories.Shared = ResolvePath(config.SkillDirectories.Shared);
        config.SkillDirectories.Project = ResolvePath(
            Path.Combine(config.ProjectPath, config.SkillDirectories.Project)
        );
        config.SkillDirectories.CopilotSkills = ResolvePath(
            Path.Combine(config.ProjectPath, config.SkillDirectories.CopilotSkills)
        );

        // Set ledger.db path
        config.LedgerDbPath = Path.Combine(config.ProjectPath, "ledger.db");

        // Resolve BMAD planning artifacts path from _bmad/bmm/config.yaml
        config.PlanningArtifactsPath = ResolvePlanningArtifacts(config.ProjectPath);

        return config;
    }

    /// <summary>
    /// Reads _bmad/bmm/config.yaml to find the planning_artifacts path.
    /// Falls back to _bmad-output/ if not found.
    /// </summary>
    private static string ResolvePlanningArtifacts(string projectPath)
    {
        var bmadConfig = Path.Combine(projectPath, "_bmad", "bmm", "config.yaml");
        if (File.Exists(bmadConfig))
        {
            try
            {
                var deserializer = new DeserializerBuilder()
                    .WithNamingConvention(UnderscoredNamingConvention.Instance)
                    .IgnoreUnmatchedProperties()
                    .Build();
                var yaml = File.ReadAllText(bmadConfig);
                var dict = deserializer.Deserialize<Dictionary<string, object>>(yaml);
                if (dict.TryGetValue("planning_artifacts", out var pa) && pa is string paStr)
                {
                    var resolved = paStr.Replace("{project-root}", projectPath).Trim();
                    // Handle relative paths
                    if (!Path.IsPathRooted(resolved))
                        resolved = Path.Combine(projectPath, resolved);
                    return Path.GetFullPath(resolved);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to parse BMAD config at '{bmadConfig}': {ex.Message}",
                    ex
                );
            }
        }

        return Path.GetFullPath(Path.Combine(projectPath, "_bmad-output"));
    }

    private static string ResolvePath(string path)
    {
        if (path.StartsWith("~/"))
            path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                path[2..]
            );
        return Path.GetFullPath(path);
    }

    public static void SaveDefault(string projectPath)
    {
        var config = new RalphLoopConfig { ProjectPath = projectPath };
        var configPath = Path.Combine(projectPath, "ralph-loop.json");
        var json = JsonSerializer.Serialize(
            config,
            new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            }
        );
        File.WriteAllText(configPath, json);
    }
}
