using GitHub.Copilot.SDK;
using RalphLoop.Agents;
using RalphLoop.Config;
using RalphLoop.Data;
using RalphLoop.Data.Models;
using RalphLoop.Data.Repositories;
using RalphLoop.UI;

namespace RalphLoop.Loop.Phases;

/// <summary>
/// Phase 1: Sprint Planning.
/// Checks ledger.db, ensures an active sprint exists, and runs bmad-sprint-planning.
/// </summary>
public class SprintPlanningPhase(
    SessionFactory factory,
    AgentRunner runner,
    SprintRepository sprints,
    ConsoleUI ui,
    RalphLoopConfig config)
{
    public async Task<Sprint> RunAsync(CancellationToken ct = default)
    {
        ui.ShowPhase("Phase 1", "Sprint Planning");

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
            activeSprint = new Sprint { Id = id, Name = name, Status = SprintStatus.Active };
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
            ui.ShowError($"No usable planning artifacts found in '{config.PlanningArtifactsPath}'.");
            ui.ShowInfo("Ralph Loop can work with any of the following (highest priority first):");
            ui.ShowInfo("  • epics.md                          ← BMAD epics breakdown (best)");
            ui.ShowInfo("  • prd.md                            ← Product Requirements Document");
            ui.ShowInfo("  • prd-distillate/                   ← BMAD PRD distillate directory");
            ui.ShowInfo("  • validation-report-prd-*.md        ← Validated PRD report");
            ui.ShowInfo("At least one of these must exist before sprint planning can run.");
            throw new InvalidOperationException(
                $"Sprint planning cannot proceed: no viable planning artifacts found in '{config.PlanningArtifactsPath}'.");
        }

        ui.ShowInfo($"Planning artifacts found in: {config.PlanningArtifactsPath}");
        if (artifacts.EpicsMd is not null)
            ui.ShowInfo($"  ✓ Epics source:        epics.md");
        if (artifacts.PrdSource is not null)
            ui.ShowInfo($"  ✓ PRD source:          {artifacts.PrdSourceLabel}");
        if (artifacts.ArchSource is not null)
            ui.ShowInfo($"  ✓ Architecture source: {artifacts.ArchSourceLabel}");

        var planningPrompt = BuildPlanningPrompt(activeSprint, artifacts);

        await ui.WithSpinnerAsync("Running BMAD sprint backlog creation...", async () =>
        {
            await runner.RunAsync(
                factory.ForScrumMaster(AgentRunner.ApproveAll(), runner.UserInputHandler()),
                planningPrompt, "Sprint Planner", ct);
        });

        // Guard: verify the agent actually created epics
        var populated = await sprints.HasEpicsAsync(activeSprint.Id);
        if (!populated)
        {
            ui.ShowError("Sprint planning agent ran but created no epics in ledger.db.");
            ui.ShowInfo("Ensure the planning artifact contains well-formed epic definitions and re-run ralph-loop.");
            throw new InvalidOperationException("BMAD sprint backlog creation produced no epics.");
        }

        return activeSprint;
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
            prompt, "Scrum Master", ct);
    }
}
