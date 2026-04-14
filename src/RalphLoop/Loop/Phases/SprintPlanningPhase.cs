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
            if (ui.Confirm("Would you like to create a new sprint now?"))
            {
                var name = ui.Ask("Sprint name:");
                var id = await sprints.InsertAsync(name);
                activeSprint = new Sprint { Id = id, Name = name, Status = SprintStatus.Active };
                ui.ShowSuccess($"Sprint '{name}' created.");
            }
            else
            {
                throw new InvalidOperationException("No active sprint — cannot continue.");
            }
        }

        ui.ShowInfo($"Active sprint: [{activeSprint.Id}] {activeSprint.Name}");

        // Skip sprint planning agent if the sprint is already populated (M18)
        var alreadyPlanned = await sprints.HasEpicsAsync(activeSprint.Id);
        if (alreadyPlanned)
        {
            ui.ShowInfo("Sprint already has epics — skipping sprint planning agent.");
            return activeSprint;
        }

        // Run sprint planning skill to display and validate the sprint
        var planningPrompt = $"""
            Run sprint planning for the active sprint: '{activeSprint.Name}' (id={activeSprint.Id}).
            Review the epics and stories in the project, display a summary, and confirm readiness.
            Check {config.PlanningArtifactsPath}/prd.md and {config.PlanningArtifactsPath}/architecture.md for context.
            """;

        await ui.WithSpinnerAsync("Running sprint planning...", async () =>
        {
            await runner.RunAsync(
                factory.ForScrumMaster(AgentRunner.ApproveAll(), runner.UserInputHandler()),
                planningPrompt, "Sprint Planner", ct);
        });

        return activeSprint;
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
