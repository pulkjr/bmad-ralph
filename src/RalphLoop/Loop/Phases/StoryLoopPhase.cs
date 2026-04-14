using GitHub.Copilot.SDK;
using RalphLoop.Agents;
using RalphLoop.Agents.Personas;
using RalphLoop.Build;
using RalphLoop.Config;
using RalphLoop.Data;
using RalphLoop.Data.Models;
using RalphLoop.Data.Repositories;
using RalphLoop.Git;
using RalphLoop.UI;

namespace RalphLoop.Loop.Phases;

/// <summary>
/// Phase 3: Story Loop.
/// For each story in the epic (top to bottom):
///   Developer → (optional agent-tui smoke) → QA → (fail loop / swarm) → test.sh → git commit
/// </summary>
public class StoryLoopPhase(
    AgentRunner runner,
    SessionFactory factory,
    PartyModeSession partyMode,
    StoryRepository storyRepo,
    LedgerDb db,
    GitManager git,
    TestScriptRunner testRunner,
    AgentTuiRunner agentTui,
    ConsoleUI ui,
    RalphLoopConfig config)
{
    public async Task RunAsync(Epic epic, IReadOnlyList<Story> stories, CancellationToken ct = default)
    {
        ui.ShowPhase("Phase 3", $"Story Loop — {stories.Count} stories");

        // Ensure epic branch exists
        await git.CreateEpicBranchAsync(epic.BranchName);

        for (var i = 0; i < stories.Count; i++)
        {
            var story = stories[i];
            ui.ShowSection($"Story {i + 1}/{stories.Count}: {story.Name}");

            // Insert or update story record
            if (story.Id == 0)
            {
                story.Id = await storyRepo.InsertAsync(epic.Id, story.Name, story.Description, story.AcceptanceCriteria, story.OrderIndex);
            }
            else
            {
                await storyRepo.UpdateStatusAsync(story.Id, StoryStatus.InProgress);
            }

            await ProcessStoryAsync(epic, story, ct);
        }
    }

    private async Task ProcessStoryAsync(Epic epic, Story story, CancellationToken ct)
    {
        var hasUxSpec = File.Exists(
            Path.Combine(config.PlanningArtifactsPath, "ux-design-specification.md"));
        var isUxStory = hasUxSpec && IsUxStory(story);

        // Accumulate failure context across rounds so the developer can learn from history
        var failureHistory = new List<string>();

        bool storyComplete = false;
        int round = 0;
        while (!storyComplete)
        {
            round++;
            if (round > config.MaxStoryRounds)
            {
                ui.ShowError($"Story '{story.Name}' exceeded max rounds ({config.MaxStoryRounds}). Marking as failed.");
                await storyRepo.UpdateStatusAsync(story.Id, StoryStatus.Failed);
                await storyRepo.AddEventAsync(story.Id, StoryEventType.QaFail,
                    $"Exceeded MaxStoryRounds ({config.MaxStoryRounds}). Manual intervention required.");
                throw new OperationCanceledException(
                    $"Story '{story.Name}' exceeded {config.MaxStoryRounds} rounds.");
            }

            await storyRepo.IncrementRoundAsync(story.Id, failed: false);
            await storyRepo.AddEventAsync(story.Id, StoryEventType.DevStart);
            ui.ShowStoryStatus(story.Name, StoryStatus.InProgress, round, story.FailCount, story.TokensUsed);

            // Step 1: Developer implements the story
            var devResult = await RunDeveloperAsync(epic, story, failureHistory, ct);
            await storyRepo.AddTokensAsync(story.Id, devResult.TokensUsed);
            await storyRepo.UpdateStatusAsync(story.Id, StoryStatus.ReadyForReview);
            await storyRepo.AddEventAsync(story.Id, StoryEventType.DevComplete, tokens: devResult.TokensUsed);

            // Step 2: agent-tui smoke test (UX stories only)
            if (isUxStory && await agentTui.IsAvailableAsync())
            {
                ui.ShowInfo("Running agent-tui smoke test...");
                var smokeResult = await agentTui.SmokeTestAsync(GetAppCommand(), ct);
                if (!smokeResult.Passed)
                {
                    ui.ShowWarning($"agent-tui smoke test failed: {smokeResult.Details}");
                    await storyRepo.AddEventAsync(story.Id, StoryEventType.UiSmokeFail,
                        smokeResult.Details);
                    var smokeFailure = $"UI Smoke Test Failed (round {round}):\n{smokeResult.Details}";
                    failureHistory.Add(smokeFailure);
                    await HandleQaFailAsync(epic, story, smokeFailure, failureHistory, ct);
                    continue;
                }
                ui.ShowSuccess("agent-tui smoke test passed.");
                await storyRepo.AddEventAsync(story.Id, StoryEventType.UiSmokePass);
            }

            // Step 3: QA review
            var qaResult = await RunQaAsync(epic, story, isUxStory, round, failureHistory, ct);
            await storyRepo.AddTokensAsync(story.Id, qaResult.TokensUsed);

            var qaPassed = IsPositiveOutcome(qaResult.Response);
            if (!qaPassed)
            {
                await storyRepo.IncrementFailCountAsync(story.Id);
                await storyRepo.AddEventAsync(story.Id, StoryEventType.QaFail,
                    qaResult.Response, qaResult.TokensUsed);

                var failCount = await storyRepo.GetFailCountAsync(story.Id);
                ui.ShowWarning($"QA failed (fail #{failCount})");

                var qaFailure = $"QA Failure (round {round}):\n{qaResult.Response}";
                failureHistory.Add(qaFailure);

                // Swarm triggers exactly at the threshold and every MaxQaFails thereafter
                if (failCount % config.MaxQaFailsBeforeSwarm == 0)
                {
                    ui.ShowWarning($"QA failed {failCount} times — launching party-mode SWARM...");
                    await RunSwarmAsync(epic, story, qaResult.Response, failCount, ct);
                }
                else
                {
                    ui.ShowInfo("Looping back to developer with QA failure report...");
                }
                continue;
            }

            // QA passed
            await storyRepo.UpdateStatusAsync(story.Id, StoryStatus.QaPassed);
            await storyRepo.AddEventAsync(story.Id, StoryEventType.QaPass,
                qaResult.Response, qaResult.TokensUsed);
            ui.ShowSuccess("QA passed!");

            // Step 4: test.sh
            var testPassed = await RunTestScriptAsync(epic, story, isUxStory, failureHistory, ct);
            if (!testPassed) continue;

            // Step 5: Commit and mark complete — wrapped in a transaction to ensure atomicity (M12)
            await using (var tx = await db.BeginTransactionAsync())
            {
                try
                {
                    await CommitStoryAsync(epic, story, ct);
                    await storyRepo.MarkCompleteAsync(story.Id);
                    await tx.CommitAsync();
                }
                catch
                {
                    await tx.RollbackAsync();
                    throw;
                }
            }
            ui.ShowSuccess($"Story '{story.Name}' complete! ✅");
            storyComplete = true;
        }
    }

    private async Task<AgentResult> RunDeveloperAsync(
        Epic epic, Story story, IReadOnlyList<string> failureHistory, CancellationToken ct)
    {
        var prompt = BuildDeveloperPrompt(epic, story, failureHistory);
        return await runner.RunAsync(
            factory.ForDeveloper(AgentRunner.ApproveAll(), runner.UserInputHandler()),
            prompt, "Developer (Amelia)", ct);
    }

    private async Task<AgentResult> RunQaAsync(
        Epic epic, Story story, bool isUxStory, int round, IReadOnlyList<string> failureHistory,
        CancellationToken ct)
    {
        var prompt = BuildQaPrompt(epic, story, isUxStory, round, failureHistory);
        return await runner.RunAsync(
            factory.ForQa(AgentRunner.ApproveAll(), runner.UserInputHandler()),
            prompt, "QA Engineer", ct);
    }

    private async Task HandleQaFailAsync(
        Epic epic, Story story, string failureReport, IReadOnlyList<string> failureHistory,
        CancellationToken ct)
    {
        var prompt = BuildDeveloperFixPrompt(epic, story, failureReport, failureHistory);
        var result = await runner.RunAsync(
            factory.ForDeveloper(AgentRunner.ApproveAll(), runner.UserInputHandler()),
            prompt, "Developer (Amelia) — Fix", ct);
        await storyRepo.AddTokensAsync(story.Id, result.TokensUsed);
    }

    private async Task RunSwarmAsync(
        Epic epic, Story story, string failureReport, int failCount, CancellationToken ct)
    {
        await storyRepo.AddEventAsync(story.Id, StoryEventType.SwarmStart);
        var hasUx = File.Exists(Path.Combine(config.PlanningArtifactsPath, "ux-design-specification.md"));
        var personas = PartyModePersonas.Build(config, hasUx);

        var prompt = $"""
            SWARM MODE — Story '{story.Name}' has failed QA {failCount} times.
            
            QA Failure Report:
            <qa-failure-report>
            {failureReport}
            </qa-failure-report>
            
            Story Description:
            <story>
            {story.Description}
            </story>
            
            PROCEDURE:
            1. QA: Restate the exact failure conditions — what was expected vs. what happened.
            2. Architect: Identify whether this is a design flaw or an implementation bug.
            3. Developer: Propose a specific code fix (files + changes).
            4. Skeptic: Challenge whether the proposed fix fully resolves the issue.
            5. Developer: Apply the fix.
            6. QA: Confirm the fix addresses the original failure.
            
            Output a final fix summary and whether QA is satisfied.
            """;

        var result = await partyMode.RunAsync(personas, prompt, $"Swarm — {story.Name}", ct);
        await storyRepo.AddTokensAsync(story.Id, result.TokensUsed);
        await storyRepo.AddEventAsync(story.Id, StoryEventType.SwarmComplete, result.Response, result.TokensUsed);
    }

    private async Task<bool> RunTestScriptAsync(
        Epic epic, Story story, bool isUxStory, IReadOnlyList<string> failureHistory,
        CancellationToken ct)
    {
        if (!testRunner.Exists)
        {
            ui.ShowWarning("test.sh not found. Asking developer to write it...");
            var writePrompt = testRunner.GetMissingScriptPrompt(isUxStory);
            var writeResult = await runner.RunAsync(
                factory.ForDeveloper(AgentRunner.ApproveAll(), runner.UserInputHandler()),
                writePrompt, "Developer (Amelia) — Write test.sh", ct);
            await storyRepo.AddTokensAsync(story.Id, writeResult.TokensUsed);
        }

        ui.ShowInfo("Running test.sh...");
        var testResult = await testRunner.RunAsync(ct);
        ui.ShowBuildOutput(testResult.Output, testResult.Passed);

        if (testResult.Passed)
        {
            await storyRepo.UpdateStatusAsync(story.Id, StoryStatus.BuildPassed);
            await storyRepo.AddEventAsync(story.Id, StoryEventType.BuildPass);
            ui.ShowSuccess("test.sh passed (exit 0).");
            return true;
        }

        // Test failed — pass output to developer, but NOT test.sh; then re-run test.sh immediately
        ui.ShowError("test.sh failed. Passing output to developer to fix the code.");
        await storyRepo.AddEventAsync(story.Id, StoryEventType.BuildFail, testResult.Output);

        var buildFailure = $"Build/Test Failure:\n{testResult.Output}";
        var allFailures = failureHistory.Append(buildFailure).ToList();

        var fixPrompt = $"""
            test.sh failed with exit code {testResult.ExitCode}. Fix the APPLICATION code to make it pass.

            IMPORTANT: You are NOT allowed to modify test.sh.

            <build-output>
            {testResult.Output}
            </build-output>

            Story being implemented: {story.Name}
            """;

        var fixResult = await runner.RunAsync(
            factory.ForDeveloper(AgentRunner.ApproveAll(), runner.UserInputHandler()),
            fixPrompt, "Developer (Amelia) — Fix Build", ct);

        await storyRepo.AddTokensAsync(story.Id, fixResult.TokensUsed);

        // Re-run test.sh immediately after the fix rather than restarting the full story loop
        ui.ShowInfo("Re-running test.sh after build fix...");
        var retestResult = await testRunner.RunAsync(ct);
        ui.ShowBuildOutput(retestResult.Output, retestResult.Passed);

        if (retestResult.Passed)
        {
            await storyRepo.UpdateStatusAsync(story.Id, StoryStatus.BuildPassed);
            await storyRepo.AddEventAsync(story.Id, StoryEventType.BuildPass);
            ui.ShowSuccess("test.sh passed after fix.");
            return true;
        }

        await storyRepo.AddEventAsync(story.Id, StoryEventType.BuildFail, retestResult.Output);
        return false; // Full loop restart needed
    }

    private async Task CommitStoryAsync(Epic epic, Story story, CancellationToken ct)
    {
        if (!config.Git.AutoCommit) return;

        var round = await storyRepo.GetRoundsAsync(story.Id);
        await git.CommitStoryAsync(story.Name, epic.Name, round);
        await storyRepo.AddEventAsync(story.Id, StoryEventType.Committed,
            $"Committed story '{story.Name}' on branch {epic.BranchName}");
        ui.ShowSuccess($"Committed: {story.Name}");
    }

    private string BuildDeveloperPrompt(Epic epic, Story story, IReadOnlyList<string> failureHistory)
    {
        var historySection = failureHistory.Count > 0
            ? $"\n\nPREVIOUS FAILURES (most recent last — do NOT repeat these mistakes):\n" +
              string.Join("\n---\n", failureHistory)
            : "";

        var acSection = string.IsNullOrWhiteSpace(story.AcceptanceCriteria)
            ? ""
            : $"\nAcceptance Criteria: {story.AcceptanceCriteria}";

        return $"""
            Implement story: '{story.Name}'

            <story>
            Description: {story.Description}{acSection}
            Epic: {epic.Name}
            </story>

            REQUIREMENTS:
            1. Implement ALL requirements described in the story.
            2. Write unit tests covering the story's acceptance criteria.
            3. Follow architecture.md conventions strictly.
            4. Do NOT modify test.sh.
            5. When finished, confirm each acceptance criterion is addressed.{historySection}
            """;
    }

    private string BuildDeveloperFixPrompt(
        Epic epic, Story story, string failureReport, IReadOnlyList<string> failureHistory)
    {
        var historySection = failureHistory.Count > 1
            ? $"\n\nFULL FAILURE HISTORY (most recent last):\n" +
              string.Join("\n---\n", failureHistory)
            : "";

        var acSection = string.IsNullOrWhiteSpace(story.AcceptanceCriteria)
            ? ""
            : $"\nAcceptance Criteria: {story.AcceptanceCriteria}";

        return $"""
            The following review/test failed for story '{story.Name}' in epic '{epic.Name}'.
            Fix the application code — DO NOT modify test.sh.

            <qa-failure-report>
            {failureReport}
            </qa-failure-report>

            <story>
            {story.Description}{acSection}
            </story>{historySection}
            """;
    }

    private string BuildQaPrompt(
        Epic epic, Story story, bool isUxStory, int round, IReadOnlyList<string> failureHistory)
    {
        var uxNote = isUxStory
            ? "\nThis is a UX story. Use agent-tui to test the TUI: launch the app, capture screenshots, and navigate user flows from ux-design-specification.md."
            : "";

        var reReviewNote = round > 1 && failureHistory.Count > 0
            ? $"\n\nThis is re-review round {round}. The previous failure was:\n<prior-failure>\n{failureHistory[^1]}\n</prior-failure>\nVerify the fix addresses the previous failure."
            : "";

        var acSection = string.IsNullOrWhiteSpace(story.AcceptanceCriteria)
            ? ""
            : $"\nAcceptance Criteria:\n{story.AcceptanceCriteria}";

        return $"""
            Review the implementation of story: '{story.Name}'

            <story>
            Description: {story.Description}{acSection}
            </story>{uxNote}{reReviewNote}

            Check:
            1. Does the implementation satisfy all acceptance criteria?
            2. Are there any bugs, edge cases, or missing error handling?
            3. Does it conform to architecture.md and project-context.md?
            4. Are there adequate tests?

            At the END of your response, emit exactly one verdict line:
            VERDICT: PASS
            or
            VERDICT: FAIL — <one-line reason>
            """;
    }

    private string GetAppCommand() =>
        !string.IsNullOrWhiteSpace(config.AppCommand)
            ? config.AppCommand
            : "./" + (
                Directory.GetFiles(config.ProjectPath, "*.csproj").Any() ? "run" :
                File.Exists(Path.Combine(config.ProjectPath, "package.json")) ? "start" :
                "app");

    private static bool IsUxStory(Story story)
    {
        var text = $"{story.Name} {story.Description}".ToLowerInvariant();
        return text.Contains("ux") || text.Contains("tui") || text.Contains("ui ");
    }

    private static bool IsPositiveOutcome(string response)
    {
        // Parse the structured VERDICT: line emitted at the end of QA responses.
        // Fall back to keyword scan only if no verdict line is present.
        var verdict = ExtractVerdict(response);
        if (verdict is not null)
            return verdict.StartsWith("PASS", StringComparison.OrdinalIgnoreCase);

        // Fallback: whole-word keyword scan (avoids false matches in explanatory text)
        if (System.Text.RegularExpressions.Regex.IsMatch(response, @"\bFAIL\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            return false;
        return System.Text.RegularExpressions.Regex.IsMatch(response, @"\bPASS\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            || System.Text.RegularExpressions.Regex.IsMatch(response, @"\bAPPROVED\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            || System.Text.RegularExpressions.Regex.IsMatch(response, @"\bLGTM\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// Extracts the last VERDICT: line from a structured agent response.
    /// Returns null if no verdict line is found.
    /// </summary>
    internal static string? ExtractVerdict(string response)
    {
        foreach (var line in response.Split('\n').Reverse())
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("VERDICT:", StringComparison.OrdinalIgnoreCase))
                return trimmed["VERDICT:".Length..].Trim();
        }
        return null;
    }
}
