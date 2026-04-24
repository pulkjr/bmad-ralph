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
    /// Per-phase enable/disable switches. Phases 1, 3, and 6 are non-disableable.
    /// </summary>
    public PhasesConfig Phases { get; set; } = new();

    /// <summary>
    /// Maximum minutes to wait for test.sh to complete before timing out.
    /// Increase for large test suites or slow CI environments.
    /// </summary>
    public int TestTimeoutMinutes { get; set; } = 10;

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

public class PhasesConfig
{
    public PhaseSprintReviewConfig SprintReview { get; set; } = new();
    public PhaseCodeQualityConfig CodeQualityGate { get; set; } = new();
    public PhaseEpicCompletionConfig EpicCompletion { get; set; } = new();
}

public class PhaseSprintReviewConfig
{
    /// <summary>When false, Phase 2.5 (Architect implementation readiness check) is skipped.</summary>
    public bool ImplementationReadiness { get; set; } = true;
}

public class PhaseCodeQualityConfig
{
    /// <summary>Master switch — when false, the entire Code Quality Gate (Phase 4) is skipped.</summary>
    public bool Enabled { get; set; } = true;

    public bool PerformancePedant { get; set; } = true; // Oliver
    public bool LegacyLibrarian { get; set; } = true; // Vera
    public bool TestArchaeologist { get; set; } = true; // Rex
    public bool CoverageCritic { get; set; } = true; // Nora
}

public class PhaseEpicCompletionConfig
{
    public bool Security { get; set; } = true;
    public bool Architect { get; set; } = true;
    public bool ProductManager { get; set; } = true;
    public bool UxDesigner { get; set; } = true;
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

    /// <summary>
    /// Model for the Phase 4 Code Quality Gate reviewers (Oliver, Vera, Rex, Nora).
    /// Defaults to the same value as <see cref="Qa"/> if not explicitly set.
    /// The QA model conflict-check (must differ from Developer) does NOT apply here.
    /// </summary>
    public string CodeQuality { get; set; } = "claude-sonnet-4.6";

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

    /// <summary>
    /// When true, sets GIT_TERMINAL_PROMPT=0 when spawning git child processes.
    /// This prevents interactive git hooks (e.g. entire's "Link this commit?"
    /// prompt) from blocking programmatic commits. The entire hook will still
    /// auto-link commits to sessions — it just won't ask interactively.
    /// Defaults to true. Set to false only if you need hooks to prompt.
    /// </summary>
    public bool SuppressInteractivePrompts { get; set; } = true;
}
