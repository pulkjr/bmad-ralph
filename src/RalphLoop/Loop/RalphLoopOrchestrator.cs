using GitHub.Copilot.SDK;
using RalphLoop.Config;
using RalphLoop.Data;
using RalphLoop.Data.Models;
using RalphLoop.Data.Repositories;
using RalphLoop.Loop.Phases;
using RalphLoop.UI;

namespace RalphLoop.Loop;

/// <summary>
/// Top-level state machine: runs all phases in sequence for each epic in the active sprint.
/// </summary>
public class RalphLoopOrchestrator(
    SprintPlanningPhase phase1,
    SprintReviewPhase phase2,
    StoryLoopPhase phase3,
    EpicCompletionPhase phase4,
    RetrospectivePhase phase5,
    EpicRepository epics,
    StoryRepository stories,
    ConsoleUI ui,
    RalphLoopConfig config
)
{
    public async Task RunAsync(CancellationToken ct = default)
    {
        ui.ShowPhase("Ralph Loop", "BMAD Agentic Development Loop starting...");

        // Phase 1: Sprint planning
        var sprint = await phase1.RunAsync(ct);

        // Get all pending epics for this sprint
        var epicList = (await epics.GetBySprintAsync(sprint.Id))
            .Where(e => e.Status is EpicStatus.Pending or EpicStatus.InProgress)
            .OrderBy(e => e.Id)
            .ToList();

        if (epicList.Count == 0)
        {
            ui.ShowWarning("No pending epics found for this sprint.");
            ui.ShowInfo("The sprint planning agent did not populate ledger.db.");
            ui.ShowInfo($"Planning artifacts directory: {config.PlanningArtifactsPath}");
            var artifacts = PlanningArtifacts.Discover(config.PlanningArtifactsPath);
            if (!artifacts.IsViable)
            {
                ui.ShowInfo("No viable planning artifacts were found. Ralph Loop accepts:");
                ui.ShowInfo("  • epics.md                       ← BMAD epics breakdown (best)");
                ui.ShowInfo("  • prd.md                         ← Product Requirements Document");
                ui.ShowInfo("  • prd-distillate/                ← BMAD PRD distillate directory");
                ui.ShowInfo("  • validation-report-prd-*.md     ← Validated PRD report");
            }
            return;
        }

        ui.ShowInfo($"Found {epicList.Count} epic(s) to process.");

        foreach (var epic in epicList)
        {
            if (ct.IsCancellationRequested)
                break;

            try
            {
                await ProcessEpicAsync(sprint, epic, ct);
            }
            catch (OperationCanceledException ex)
            {
                ui.ShowWarning($"Epic '{epic.Name}' was paused: {ex.Message}");
                ui.ShowInfo("Re-run ralph-loop to resume from the current epic.");
                break;
            }
        }

        ui.ShowSuccess("Ralph Loop completed! 🚀");
    }

    private async Task ProcessEpicAsync(Sprint sprint, Epic epic, CancellationToken ct)
    {
        ui.ShowSection($"═══ Epic: {epic.Name} ═══");

        // Phase 2: sprint review + implementation readiness
        var startedEpic = await phase2.RunAsync(sprint, epic, ct);

        // Load or create stories for this epic
        var storyList = (await stories.GetByEpicAsync(epic.Id))
            .Where(s => s.Status is not StoryStatus.Complete)
            .OrderBy(s => s.OrderIndex)
            .ThenBy(s => s.Id)
            .ToList();

        if (storyList.Count == 0)
        {
            ui.ShowWarning($"No incomplete stories found for epic '{epic.Name}'.");
            ui.ShowInfo("Stories should be in ledger.db. Add them or re-run the sprint planning.");
        }
        else
        {
            // Phase 3: story-by-story development loop
            await phase3.RunAsync(startedEpic, storyList, ct);
        }

        // Phase 4: epic completion reviews
        await phase4.RunAsync(startedEpic, ct);

        // Phase 5: retrospective + merge
        await phase5.RunAsync(startedEpic, sprint, ct);
    }
}
