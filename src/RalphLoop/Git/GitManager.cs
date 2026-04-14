using System.Diagnostics;

namespace RalphLoop.Git;

/// <summary>
/// Manages git operations for the ralph loop: branch per epic, entire.io commits, FF merge.
/// </summary>
public class GitManager(string projectPath)
{
    public async Task<bool> IsEntireEnabledAsync()
    {
        var result = await RunAsync("entire", "status", projectPath);
        return result.ExitCode == 0;
    }

    public async Task EnableEntireAsync()
    {
        await RunAsync("entire", "enable --agent copilot-cli --telemetry=false", projectPath);
    }

    public async Task CreateEpicBranchAsync(string branchName)
    {
        // Checkout existing branch if it exists; create it only if it doesn't
        var checkout = await RunAsync("git", $"checkout {branchName}", projectPath);
        if (checkout.ExitCode != 0)
        {
            var create = await RunAsync("git", $"checkout -b {branchName}", projectPath);
            if (create.ExitCode != 0)
                throw new InvalidOperationException(
                    $"Failed to create branch '{branchName}': {create.StdErr}");
        }
    }

    public async Task CommitStoryAsync(string storyName, string epicName, int round)
    {
        var message = FormatCommitMessage(storyName, epicName, round);

        var addResult = await RunAsync("git", "add -A", projectPath);
        if (addResult.ExitCode != 0)
            throw new InvalidOperationException($"git add failed: {addResult.StdErr}");

        var commitResult = await RunWithStdinAsync("git", "commit -F -", projectPath, message);
        if (commitResult.ExitCode != 0)
            throw new InvalidOperationException($"git commit failed: {commitResult.StdErr}");
    }

    public async Task MergeEpicToMainAsync(string branchName)
    {
        // Detect the default branch (main, master, develop, trunk, etc.)
        var symRef = await RunAsync("git", "symbolic-ref refs/remotes/origin/HEAD --short", projectPath);
        var defaultBranch = symRef.ExitCode == 0
            ? symRef.StdOut.Trim().Replace("origin/", "")
            : "main";

        var checkoutResult = await RunAsync("git", $"checkout {defaultBranch}", projectPath);
        if (checkoutResult.ExitCode != 0)
            throw new InvalidOperationException(
                $"Cannot checkout default branch '{defaultBranch}': {checkoutResult.StdErr}");

        // Fast-forward merge
        var mergeResult = await RunAsync("git", $"merge --ff-only {branchName}", projectPath);
        if (mergeResult.ExitCode != 0)
            throw new InvalidOperationException(
                $"Fast-forward merge failed for branch {branchName}. " +
                $"Output: {mergeResult.StdErr}");
    }

    public async Task<string> GetCurrentBranchAsync()
    {
        var result = await RunAsync("git", "rev-parse --abbrev-ref HEAD", projectPath);
        return result.StdOut.Trim();
    }

    /// <summary>
    /// Returns a summary of files changed between the current branch and the default branch.
    /// Used to give Phase 4 reviewers context about what was implemented.
    /// </summary>
    public async Task<string> GetChangedFilesSummaryAsync()
    {
        // Try to get diff stat vs origin/HEAD; fall back to last 10 commits
        var symRef = await RunAsync("git", "symbolic-ref refs/remotes/origin/HEAD --short", projectPath);
        var defaultBranch = symRef.ExitCode == 0
            ? symRef.StdOut.Trim().Replace("origin/", "")
            : "main";

        var diffResult = await RunAsync("git", $"diff --stat {defaultBranch}...HEAD", projectPath);
        if (diffResult.ExitCode == 0 && !string.IsNullOrWhiteSpace(diffResult.StdOut))
            return diffResult.StdOut.Trim();

        // Fallback: recent commit log
        var logResult = await RunAsync("git", "log --oneline -10", projectPath);
        return logResult.ExitCode == 0 ? logResult.StdOut.Trim() : "(unable to get change summary)";
    }

    /// <summary>
    /// Formats a commit message following the structured entire.io format.
    /// </summary>
    public static string FormatCommitMessage(string storyName, string epicName, int round)
    {
        var scope = epicName.ToLower().Replace(" ", "-");
        var subject = $"feat({scope}): complete story — {storyName}";
        return $"""
            {subject}

            [Intent]: Implement story '{storyName}' as part of epic '{epicName}' (round {round}).
            [Approach]: Implemented via BMAD dev → QA → build loop. Story passed QA and test.sh.
            [Outcome]: Story is complete and ready for epic review.

            Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>
            """;
    }

    private static string EscapeForShell(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");

    private static async Task<ProcessResult> RunWithStdinAsync(
        string executable, string args, string workDir, string stdin)
    {
        var psi = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = args,
            WorkingDirectory = workDir,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start {executable}");

        await process.StandardInput.WriteAsync(stdin);
        process.StandardInput.Close();

        var stdOut = await process.StandardOutput.ReadToEndAsync();
        var stdErr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return new ProcessResult(process.ExitCode, stdOut, stdErr);
    }

    private static async Task<ProcessResult> RunAsync(string executable, string args, string workDir)
    {
        var psi = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = args,
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start {executable}");

        var stdOut = await process.StandardOutput.ReadToEndAsync();
        var stdErr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return new ProcessResult(process.ExitCode, stdOut, stdErr);
    }

    private record ProcessResult(int ExitCode, string StdOut, string StdErr);
}
