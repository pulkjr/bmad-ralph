using GitHub.Copilot.SDK;
using RalphLoop.Agents;
using RalphLoop.Config;
using RalphLoop.Data;
using RalphLoop.Data.Models;
using RalphLoop.Data.Repositories;
using RalphLoop.Git;
using RalphLoop.UI;

namespace RalphLoop.Loop.Phases;

/// <summary>
/// Phase 5: Retrospective + rollover.
/// Runs bmad-retrospective, saves notes, fast-forward merges epic branch to main.
/// </summary>
public class RetrospectivePhase(
    AgentRunner runner,
    SessionFactory factory,
    StoryRepository storyRepo,
    GitManager git,
    ConsoleUI ui,
    RalphLoopConfig config)
{
    public async Task RunAsync(Epic epic, Data.Models.Sprint sprint, CancellationToken ct = default)
    {
        ui.ShowPhase("Phase 5", $"Retrospective — {epic.Name}");

        var retroPrompt = $"""
            Run a sprint retrospective for epic '{epic.Name}' in sprint '{sprint.Name}'.
            
            Cover:
            1. What went well?
            2. What could be improved?
            3. Any recurring issues (e.g. repeated QA failures)?
            4. Action items for the next sprint.
            
            Provide a concise summary suitable for storing in the project ledger.
            """;

        var retroResult = await runner.RunAsync(
            factory.ForScrumMaster(AgentRunner.ApproveAll(), runner.UserInputHandler()),
            retroPrompt, "Retrospective", ct);

        // Save retrospective to DB
        await storyRepo.InsertRetrospectiveAsync(epic.Id, retroResult.Response);
        ui.ShowSuccess("Retrospective saved.");

        // Fast-forward merge
        if (config.Git.AutoCommit && !string.IsNullOrEmpty(epic.BranchName))
        {
            ui.ShowInfo($"Merging {epic.BranchName} → main (fast-forward)...");
            try
            {
                await git.MergeEpicToMainAsync(epic.BranchName);
                ui.ShowSuccess($"Branch {epic.BranchName} merged to main. 🚀");
            }
            catch (InvalidOperationException ex)
            {
                ui.ShowWarning($"Fast-forward merge failed: {ex.Message}");
                ui.ShowInfo("You may need to merge manually.");
            }
        }

        // Mark sprint complete if all epics are done
        ui.ShowSuccess($"Epic '{epic.Name}' fully complete. Rolling to next epic/sprint...");
    }
}
