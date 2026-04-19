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
    public bool DebugLog { get; set; } = false;

    /// <summary>
    /// Controls where story content (FRs, NFRs, ACs) is stored.
    /// "sqlite" (default): all story content lives in ledger.db.
    /// "file": story content lives in BMAD story .md files and sprint-status.yaml;
    ///         ledger.db is used only for the operational ledger (rounds, tokens, events).
    /// </summary>
    public string StorageMode { get; set; } = StorageModes.Sqlite;

    /// <summary>
    /// Maximum number of QA failure entries to include in developer prompts.
    /// Older entries are dropped with an omitted-count note to control context size.
    /// </summary>
    public int MaxFailureHistoryEntries { get; set; } = 2;

    /// <summary>
    /// Compaction thresholds for infinite sessions.
    /// </summary>
    public CompactionConfig Compaction { get; set; } = new();

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

    /// <summary>
    /// Path where BMAD implementation artifacts live (sprint-status.yaml, story .md files).
    /// Resolved from _bmad/bmm/config.yaml (implementation_artifacts key).
    /// Falls back to PlanningArtifactsPath if not configured.
    /// </summary>
    [JsonIgnore]
    public string ImplementationArtifactsPath { get; set; } = "";
}

public static class StorageModes
{
    public const string Sqlite = "sqlite";
    public const string File = "file";
}

public class CompactionConfig
{
    /// <summary>Background compaction starts at this fraction of the context window (default 70%).</summary>
    public double BackgroundThreshold { get; set; } = 0.70;

    /// <summary>Blocking compaction starts at this fraction of the context window (default 88%).</summary>
    public double BlockingThreshold { get; set; } = 0.88;
}

public class SkillDirectoriesConfig
{
    public string Shared { get; set; } = "~/.bmad/skills";
    public string Project { get; set; } = ".bmad-core/skills";

    /// <summary>
    /// GitHub Copilot skill install location (project-relative).
    /// Skills installed via <c>npx bmad-method install</c> with the Copilot target
    /// land here automatically.
    /// </summary>
    public string CopilotSkills { get; set; } = ".github/skills";
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

    /// <summary>
    /// Seconds to wait for a git operation before cancelling.
    /// Prevents indefinite hangs caused by slow pre-commit hooks (e.g. entire).
    /// </summary>
    public int TimeoutSeconds { get; set; } = 60;
}
