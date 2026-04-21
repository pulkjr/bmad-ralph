using GitHub.Copilot.SDK;
using RalphLoop.Agents;
using RalphLoop.Config;
using RalphLoop.Data;
using RalphLoop.Data.FileStore;
using RalphLoop.Data.Models;
using RalphLoop.Data.Repositories;
using RalphLoop.Git;
using RalphLoop.UI;

namespace RalphLoop.Loop.Phases;

/// <summary>
/// Phase 1: Sprint Planning.
/// Checks ledger.db, ensures an active sprint exists, and runs bmad-sprint-planning.
/// In file-based storage mode, reads sprint-status.yaml and creates SQLite records from it.
/// </summary>
public class SprintPlanningPhase(
    SessionFactory factory,
    AgentRunner runner,
    SprintRepository sprints,
    EpicRepository epics,
    StoryRepository storyRepo,
    FileStoreContext fileStore,
    ConsoleUI ui,
    RalphLoopConfig config
)
{
    public async Task<Sprint> RunAsync(CancellationToken ct = default)
    {
        ui.ShowPhase("Phase 1", "Sprint Planning");

        if (config.StorageMode == StorageModes.File)
            return await RunFileModeAsync(ct);

        return await RunSqliteModeAsync(ct);
    }

    // ─── SQLite mode (existing behaviour, completely unchanged) ───────────────

    private async Task<Sprint> RunSqliteModeAsync(CancellationToken ct)
    {
        // Ensure ledger.db exists (already opened by LedgerDb)
        if (!File.Exists(config.LedgerDbPath))
        {
            ui.ShowWarning("ledger.db not found. Running scrum-master skill to create it...");
            await RunScrumMasterSkillAsync(ct);
        }

        // Find active sprint
        var activeSprint = await sprints.GetActiveSprintAsync();
        if (activeSprint is null)
        {
            ui.ShowWarning("No active sprint found in ledger.db.");
            var all = await sprints.GetAllAsync();
            var name = $"Sprint {all.Count + 1}";
            var id = await sprints.InsertAsync(name);
            activeSprint = new Sprint
            {
                Id = id,
                Name = name,
                Status = SprintStatus.Active,
            };
            ui.ShowSuccess($"Sprint '{name}' created.");
        }

        ui.ShowInfo($"Active sprint: [{activeSprint.Id}] {activeSprint.Name}");

        // Skip sprint planning agent if the sprint is already populated (M18)
        var alreadyPlanned = await sprints.HasEpicsAsync(activeSprint.Id);
        if (alreadyPlanned)
        {
            ui.ShowInfo("Sprint already has epics — skipping sprint planning agent.");
            return activeSprint;
        }

        // Discover whatever planning artifacts are present (flexible BMAD layout)
        var artifacts = PlanningArtifacts.Discover(config.PlanningArtifactsPath);

        if (!artifacts.IsViable)
        {
            ui.ShowError(
                $"No usable planning artifacts found in '{config.PlanningArtifactsPath}'."
            );
            ui.ShowInfo("Ralph Loop can work with any of the following (highest priority first):");
            ui.ShowInfo("  • epics.md                          ← BMAD epics breakdown (best)");
            ui.ShowInfo("  • prd.md                            ← Product Requirements Document");
            ui.ShowInfo("  • prd-distillate/                   ← BMAD PRD distillate directory");
            ui.ShowInfo("  • validation-report-prd-*.md        ← Validated PRD report");
            ui.ShowInfo("At least one of these must exist before sprint planning can run.");
            throw new InvalidOperationException(
                $"Sprint planning cannot proceed: no viable planning artifacts found in '{config.PlanningArtifactsPath}'."
            );
        }

        ui.ShowInfo($"Planning artifacts found in: {config.PlanningArtifactsPath}");
        if (artifacts.EpicsMd is not null)
            ui.ShowInfo($"  ✓ Epics source:        epics.md");
        if (artifacts.PrdSource is not null)
            ui.ShowInfo($"  ✓ PRD source:          {artifacts.PrdSourceLabel}");
        if (artifacts.ArchSource is not null)
            ui.ShowInfo($"  ✓ Architecture source: {artifacts.ArchSourceLabel}");

        var planningPrompt = BuildPlanningPrompt(activeSprint, artifacts);

        await ui.WithSpinnerAsync(
            "Running BMAD sprint backlog creation...",
            async () =>
            {
                await runner.RunAsync(
                    factory.ForScrumMaster(AgentRunner.ApproveAll(), runner.UserInputHandler()),
                    planningPrompt,
                    "Sprint Planner",
                    ct
                );
            }
        );

        // Guard: verify the agent actually created epics
        var populated = await sprints.HasEpicsAsync(activeSprint.Id);
        if (!populated)
        {
            ui.ShowError("Sprint planning agent ran but created no epics in ledger.db.");
            ui.ShowInfo(
                "Ensure the planning artifact contains well-formed epic definitions and re-run ralph-loop."
            );
            throw new InvalidOperationException("BMAD sprint backlog creation produced no epics.");
        }

        return activeSprint;
    }

    // ─── File mode (new) ─────────────────────────────────────────────────────

    private async Task<Sprint> RunFileModeAsync(CancellationToken ct)
    {
        var yamlPath = Path.Combine(config.ImplementationArtifactsPath, "sprint-status.yaml");

        // Step a: locate or create sprint-status.yaml
        if (!File.Exists(yamlPath))
        {
            ui.ShowWarning(
                $"sprint-status.yaml not found in '{config.ImplementationArtifactsPath}'. "
                    + "Running bmad-sprint-planning to create it..."
            );
            await RunSprintPlanningSkillAsync(yamlPath, ct);
        }

        if (!File.Exists(yamlPath))
            throw new InvalidOperationException(
                $"bmad-sprint-planning did not produce sprint-status.yaml at '{yamlPath}'. "
                    + "Check the agent output and ensure the skill writes to that path."
            );

        // Step b: load the yaml and configure FileStoreContext
        fileStore.Initialize(yamlPath);
        var sprintStatus = await fileStore.GetAsync();

        // Step c: find or create active sprint
        var activeSprint = await sprints.GetActiveSprintAsync();
        if (activeSprint is null)
        {
            var all = await sprints.GetAllAsync();
            var name = $"Sprint {all.Count + 1}";
            var id = await sprints.InsertAsync(name);
            activeSprint = new Sprint
            {
                Id = id,
                Name = name,
                Status = SprintStatus.Active,
            };
            ui.ShowSuccess($"Sprint '{name}' created.");
        }
        ui.ShowInfo($"Active sprint: [{activeSprint.Id}] {activeSprint.Name}");

        // Skip if already populated (resume scenario)
        if (await sprints.HasEpicsAsync(activeSprint.Id))
        {
            ui.ShowInfo("Sprint already has epics — skipping file-mode planning import.");
            return activeSprint;
        }

        // Step d: compute which epics have at least one non-done story (needed to derive
        // the correct SQLite epic status before inserting).
        var epicHasNonDoneStory = BuildEpicNonDoneMap(sprintStatus.GetStoriesOnly());

        // Step d: create epic records from YAML with status derived from YAML state
        var epicKeyToId = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var epicEntry in sprintStatus.GetEpicsOnly())
        {
            var epicName = ToHumanName(epicEntry.Key);
            var hasNonDone = epicHasNonDoneStory.TryGetValue(epicEntry.Key, out var nd) && nd;
            var epicStatus = MapYamlStatusToEpicStatus(epicEntry.Status, hasNonDone);
            var epicId = await epics.InsertAsync(activeSprint.Id, epicName, "", epicStatus);

            // For in_progress epics Phase 2 (SprintReviewPhase) will be skipped, which
            // is normally where branch_name gets set via MarkStartedAsync. Set it here
            // so Phase 3 can create the git branch without hitting an empty-name error.
            if (epicStatus == EpicStatus.InProgress)
            {
                var branchName = GitManager.SlugifyBranchName($"epic/{epicName}");
                await epics.MarkStartedAsync(epicId, branchName);
            }

            epicKeyToId[epicEntry.Key] = epicId;
            ui.ShowInfo($"  Epic: [{epicId}] {epicName} [{epicStatus}]");
        }

        if (epicKeyToId.Count == 0)
        {
            throw new InvalidOperationException(
                $"sprint-status.yaml has no epic entries. "
                    + "Ensure the YAML uses keys starting with 'epic-' (e.g. 'epic-1', 'epic-auth')."
            );
        }

        // Use the first epic as fallback if story keys don't match a specific epic
        var fallbackEpicId = epicKeyToId.Values.First();

        // Step e: process stories
        int orderIndex = 0;
        foreach (var storyEntry in sprintStatus.GetStoriesOnly())
        {
            var storyKey = storyEntry.Key;
            var storyStatus = MapYamlStatusToStoryStatus(storyEntry.Status);
            var filePath = sprintStatus.ResolveStoryFilePath(storyKey);

            // Step e.1: create story file if backlog + missing (never for done stories)
            if (
                !string.Equals(
                    storyStatus,
                    StoryStatus.Complete,
                    StringComparison.OrdinalIgnoreCase
                )
                && string.Equals(storyEntry.Status, "backlog", StringComparison.OrdinalIgnoreCase)
                && !File.Exists(filePath)
            )
            {
                ui.ShowInfo($"  Story '{storyKey}' is backlog — running bmad-create-story...");
                await RunCreateStorySkillAsync(storyKey, filePath, ct);

                if (File.Exists(filePath))
                    await sprintStatus.UpdateStatusAsync(storyKey, "ready-for-dev");
                else
                    ui.ShowWarning(
                        $"  bmad-create-story did not create '{filePath}' — story skipped."
                    );
            }

            // Skip non-done stories whose file is still missing (done stories are always inserted)
            if (
                !string.Equals(
                    storyStatus,
                    StoryStatus.Complete,
                    StringComparison.OrdinalIgnoreCase
                ) && !File.Exists(filePath)
            )
            {
                ui.ShowWarning(
                    $"  Story file not found for '{storyKey}' at '{filePath}' — skipping."
                );
                continue;
            }

            // Step e.2: read title and short description from file (if it exists)
            var title = ToHumanName(storyKey);
            var shortDesc = string.Empty;
            if (File.Exists(filePath))
            {
                var fileTitle = await StoryFileManager.ReadTitleAsync(filePath);
                if (!string.IsNullOrWhiteSpace(fileTitle))
                    title = fileTitle;
                shortDesc = await StoryFileManager.ReadShortDescriptionAsync(filePath);
            }

            // Step e.3: check for existing ralph-story-id (resume after ledger.db delete)
            long storyId;
            if (File.Exists(filePath))
            {
                var existingId = await StoryFileManager.ReadRalphStoryIdAsync(filePath);
                if (existingId.HasValue)
                {
                    ui.ShowInfo(
                        $"  Story '{storyKey}': stale ralph-story-id {existingId} found — reassigning."
                    );
                }
            }

            // Determine epic: infer from story key prefix (e.g., "1-1-..." → epic "epic-1")
            var epicId = InferEpicId(storyKey, epicKeyToId, fallbackEpicId);

            storyId = await storyRepo.InsertFileStoryAsync(
                epicId,
                title,
                File.Exists(filePath) ? filePath : string.Empty,
                orderIndex++,
                shortDescription: shortDesc,
                status: storyStatus
            );

            // Step e.4: write ralph-story-id front-matter (only if file exists)
            if (File.Exists(filePath))
                await StoryFileManager.WriteRalphStoryIdAsync(filePath, storyId);
            ui.ShowInfo(
                $"  Story [{storyId}] {title} [{storyStatus}]"
                    + (File.Exists(filePath) ? $" → {Path.GetFileName(filePath)}" : " (no file)")
            );
        }

        // Guard: at least one story must exist
        if (!await sprints.HasEpicsAsync(activeSprint.Id))
            throw new InvalidOperationException(
                "File-mode sprint planning produced no epics. "
                    + "Check sprint-status.yaml for 'epic-*' keys."
            );

        return activeSprint;
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private async Task RunSprintPlanningSkillAsync(string targetYamlPath, CancellationToken ct)
    {
        var prompt = $"""
            You are the Sprint Planner. Create a BMAD sprint plan.

            Planning artifacts are in: {config.PlanningArtifactsPath}
            Read epics.md (or prd.md if epics.md is absent) and architecture.md.

            Write the sprint plan to: {targetYamlPath}

            The sprint-status.yaml must follow this structure:
              story_location: ./stories

              development_status:
                epic-1: backlog
                1-1-first-story: backlog
                1-2-second-story: backlog
                epic-2: backlog
                2-1-another-story: backlog

            Epic keys start with 'epic-'. Story keys follow the pattern <epic-number>-<story-number>-<slug>.
            """;

        await runner.RunAsync(
            factory.ForScrumMaster(AgentRunner.ApproveAll(), runner.UserInputHandler()),
            prompt,
            "Sprint Planner (file mode)",
            ct
        );
    }

    private async Task RunCreateStorySkillAsync(
        string storyKey,
        string targetFilePath,
        CancellationToken ct
    )
    {
        var prompt = $"""
            You are the Story Creator. Create a BMAD story file.

            Story key: {storyKey}
            Target file path: {targetFilePath}
            Planning artifacts: {config.PlanningArtifactsPath}

            1. Read the epic breakdown from epics.md (or prd.md if absent) in the planning artifacts.
            2. Find the story matching key '{storyKey}'.
            3. Create a story .md file at: {targetFilePath}
               following the BMAD story template with these sections:
               - # <Story Title> (H1)
               - Status: ready-for-dev
               - ## Story (description)
               - ## Acceptance Criteria
               - ## Dev Notes
               - ## Dev Agent Record
            """;

        await runner.RunAsync(
            factory.ForStoryRefiner(AgentRunner.ApproveAll(), runner.UserInputHandler()),
            prompt,
            $"Story Creator — {storyKey}",
            ct
        );
    }

    /// <summary>
    /// Infers the SQLite epic ID for a story key.
    /// Story keys follow the pattern <epic-index>-<story-index>-<slug>.
    /// We try to match the first segment to an epic key ("epic-1", "epic-<n>", etc.)
    /// and fall back to <paramref name="fallbackEpicId"/> if no match is found.
    /// </summary>
    private static long InferEpicId(
        string storyKey,
        Dictionary<string, long> epicKeyToId,
        long fallbackEpicId
    )
    {
        // Try to extract the epic index from "1-2-slug" → "1" → look for "epic-1"
        var parts = storyKey.Split('-');
        if (parts.Length >= 2 && long.TryParse(parts[0], out var epicIndex))
        {
            var candidate = $"epic-{epicIndex}";
            if (epicKeyToId.TryGetValue(candidate, out var matched))
                return matched;
        }
        return fallbackEpicId;
    }

    /// <summary>Converts a kebab-case YAML key to a human-readable name.</summary>
    internal static string ToHumanName(string key)
    {
        // "epic-1" → "Epic 1", "epic-auth-service" → "Epic Auth Service"
        var words = key.Split('-').Select(w => char.ToUpperInvariant(w[0]) + w[1..]);
        return string.Join(' ', words);
    }

    /// <summary>
    /// Maps a YAML story status string to the SQLite <see cref="StoryStatus"/> constant.
    /// </summary>
    /// <remarks>
    /// Only <c>done</c> stories map to <see cref="StoryStatus.Complete"/>; every other
    /// value (backlog, ready-for-dev, in-progress, …) maps to
    /// <see cref="StoryStatus.Pending"/> so the loop can pick them up.
    /// </remarks>
    internal static string MapYamlStatusToStoryStatus(string yamlStatus) =>
        string.Equals(yamlStatus, "done", StringComparison.OrdinalIgnoreCase)
            ? StoryStatus.Complete
            : StoryStatus.Pending;

    /// <summary>
    /// Maps a YAML epic status string + whether the epic has any non-done child stories
    /// to the SQLite <see cref="EpicStatus"/> constant.
    /// </summary>
    /// <remarks>
    /// <list type="table">
    ///   <listheader><term>YAML</term><term>Has non-done stories?</term><term>SQLite</term></listheader>
    ///   <item><term>done</term><term>yes</term><term>in_progress (Phase 2 skipped; go to Phase 3)</term></item>
    ///   <item><term>done</term><term>no</term><term>complete (skip entirely)</term></item>
    ///   <item><term>in-progress</term><term>—</term><term>in_progress</term></item>
    ///   <item><term>backlog / anything else</term><term>—</term><term>pending</term></item>
    /// </list>
    /// </remarks>
    internal static string MapYamlStatusToEpicStatus(string yamlStatus, bool hasNonDoneStories)
    {
        if (string.Equals(yamlStatus, "done", StringComparison.OrdinalIgnoreCase))
            return hasNonDoneStories ? EpicStatus.InProgress : EpicStatus.Complete;

        if (string.Equals(yamlStatus, "in-progress", StringComparison.OrdinalIgnoreCase))
            return EpicStatus.InProgress;

        return EpicStatus.Pending;
    }

    /// <summary>
    /// Pre-computes, for each epic key, whether it owns at least one non-done story.
    /// Uses the same epic-key inference logic as <see cref="InferEpicId"/>.
    /// </summary>
    internal static Dictionary<string, bool> BuildEpicNonDoneMap(
        IReadOnlyList<StatusEntry> storyEntries
    )
    {
        var result = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var story in storyEntries)
        {
            var parts = story.Key.Split('-');
            if (parts.Length < 2 || !long.TryParse(parts[0], out var epicIndex))
                continue;

            var epicKey = $"epic-{epicIndex}";
            var isDone = string.Equals(story.Status, "done", StringComparison.OrdinalIgnoreCase);

            if (!result.TryGetValue(epicKey, out var current))
                result[epicKey] = !isDone;
            else if (!isDone)
                result[epicKey] = true;
        }
        return result;
    }

    private string BuildPlanningPrompt(Sprint sprint, PlanningArtifacts artifacts)
    {
        // When epics.md exists the breakdown is already done — give the agent a focused prompt.
        // Otherwise ask it to decompose the PRD source into epics.
        var step1 = artifacts.EpicsMd is not null
            ? $"""
                Step 1 — Read the pre-defined epic breakdown:
                  - Epics: {artifacts.EpicsMd}
                """
            : $"""
                Step 1 — Read the planning artifacts and decompose into epics:
                  - PRD source: {artifacts.PrdSource}
                Each epic should represent a coherent feature area. Each story must be independently
                deliverable and testable with clear acceptance criteria.
                """;

        var archLine = artifacts.ArchSource is not null
            ? $"  - Architecture: {artifacts.ArchSource}"
            : "  (no architecture source found — proceed without it)";

        return $"""
            You are the Scrum Master performing BMAD sprint backlog creation.
            Active sprint: '{sprint.Name}' (sprint_id={sprint.Id})

            {step1}

            Step 2 — Read architecture context (for sizing and technical constraints):
            {archLine}

            Step 3 — Populate '{config.LedgerDbPath}' using these exact SQL statements.
            Use the sqlite3 tool or run raw SQL — do NOT use any ORM or application code.

              INSERT INTO epics (sprint_id, name, description, status)
              VALUES ({sprint.Id}, '<epic name>', '<description>', 'pending');

              -- Capture last_insert_rowid() as <epic_id> for the epic's stories:
              INSERT INTO stories (epic_id, name, description, acceptance_criteria, order_index, status, start_time)
              VALUES (<epic_id>, '<story name>', '<description>', '<acceptance criteria>', <1,2,3...>, 'pending', NULL);

            IMPORTANT:
              • story status MUST be 'pending' (not 'in_progress') so the Ralph Loop can pick them up
              • Every epic must have at least one story
              • acceptance_criteria must be non-empty

            Step 4 — After inserting all records, display a summary table of created epics and
            stories, then confirm sprint readiness.
            """;
    }

    private async Task RunScrumMasterSkillAsync(CancellationToken ct)
    {
        var prompt = $"""
            You are the Scrum Master. Create a new ledger.db SQLite database at '{config.LedgerDbPath}'.
            The database should contain tables for: sprints, epics, stories, story_events, retrospectives.
            Guide the user through creating their first sprint interactively.
            """;

        await runner.RunAsync(
            factory.ForDeveloper(AgentRunner.ApproveAll(), runner.UserInputHandler()),
            prompt,
            "Scrum Master",
            ct
        );
    }
}
