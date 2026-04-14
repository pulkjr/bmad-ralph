using System.Text.Json.Serialization;

namespace RalphLoop.Config;

public class RalphLoopConfig
{
    public string ProjectPath { get; set; } = ".";
    public SkillDirectoriesConfig SkillDirectories { get; set; } = new();
    public ModelsConfig Models { get; set; } = new();
    public GitConfig Git { get; set; } = new();
    public int MaxQaFailsBeforeSwarm { get; set; } = 3;
    public int MaxStoryRounds { get; set; } = 10;
    public bool EnableAgentTui { get; set; } = true;

    /// <summary>
    /// Overrides the default app run command heuristic.
    /// If empty, the command is auto-detected from project type.
    /// </summary>
    public string AppCommand { get; set; } = "";

    // Resolved at load time — not serialized to JSON
    [JsonIgnore]
    public string LedgerDbPath { get; set; } = "";

    [JsonIgnore]
    public string PlanningArtifactsPath { get; set; } = "";
}

public class SkillDirectoriesConfig
{
    public string Shared { get; set; } = "~/.bmad/skills";
    public string Project { get; set; } = ".bmad-core/skills";
}

public class ModelsConfig
{
    public string Default { get; set; } = "gpt-5";
    public string Developer { get; set; } = "gpt-5.3-codex";
    public string Architect { get; set; } = "claude-sonnet-4.6";
    public string ProductManager { get; set; } = "claude-sonnet-4.6";
    public string Qa { get; set; } = "claude-sonnet-4.6";
    public string Security { get; set; } = "gpt-5";
    public string TechWriter { get; set; } = "claude-sonnet-4.5";
    public string UxDesigner { get; set; } = "claude-sonnet-4.5";
    public string PartyMode { get; set; } = "claude-sonnet-4.6";
}

public class GitConfig
{
    public bool AutoCommit { get; set; } = true;
    public string MergeStrategy { get; set; } = "fast-forward";
    public bool UseEntire { get; set; } = true;
}
