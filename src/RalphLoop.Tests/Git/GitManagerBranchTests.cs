using System.Diagnostics;
using RalphLoop.Git;
using Xunit;

namespace RalphLoop.Tests.Git;

/// <summary>
/// Integration tests for <see cref="GitManager.CreateEpicBranchAsync"/> that require
/// a real git repository. Each test initialises a temporary repo, performs the scenario,
/// and cleans up afterwards.
/// </summary>
public sealed class GitManagerBranchTests : IDisposable
{
    private readonly string _repoPath;

    public GitManagerBranchTests()
    {
        _repoPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_repoPath);

        RunGit("init");
        RunGit("config user.email test@test.com");
        RunGit("config user.name Test");

        // Create an initial commit on the default branch so the repo is valid
        File.WriteAllText(Path.Combine(_repoPath, "readme.txt"), "initial");
        RunGit("add .");
        RunGit("commit -m initial");
    }

    public void Dispose() => Directory.Delete(_repoPath, recursive: true);

    // ── Already on the target branch — must be a no-op ───────────────────────

    [Fact]
    public async Task CreateEpicBranchAsync_AlreadyOnTargetBranch_ReturnsWithoutError()
    {
        // Arrange: create and switch to a new branch
        RunGit("checkout -b epic-my-feature");

        var sut = new GitManager(_repoPath);

        // Act + Assert: calling it again for the same branch must not throw
        await sut.CreateEpicBranchAsync("epic-my-feature");

        // Verify we are still on the same branch
        var branch = await sut.GetCurrentBranchAsync();
        Assert.Equal("epic-my-feature", branch);
    }

    // ── Dirty working tree when branch already exists — must give helpful hint ─

    [Fact]
    public async Task CreateEpicBranchAsync_BranchExistsWithDirtyWorkingTree_ThrowsWithHelpfulHint()
    {
        // Arrange: note the default branch BEFORE switching away from it
        var defaultBranch = GetDefaultBranch();

        // Create epic-dirty and commit a different version of readme.txt on it
        RunGit("checkout -b epic-dirty");
        File.WriteAllText(Path.Combine(_repoPath, "readme.txt"), "branch version");
        RunGit("add .");
        RunGit("commit -m \"epic commit\"");

        // Switch back to the default branch (main/master) and dirty readme.txt without committing
        RunGit($"checkout {defaultBranch}");
        File.WriteAllText(Path.Combine(_repoPath, "readme.txt"), "uncommitted local change");

        var sut = new GitManager(_repoPath);

        // Act + Assert: must throw InvalidOperationException mentioning commit or stash
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.CreateEpicBranchAsync("epic-dirty")
        );

        Assert.Contains("commit", ex.Message, StringComparison.OrdinalIgnoreCase);
        // Regression: v0.1.5 fell through to `git checkout -b` and surfaced the cryptic
        // git error "a branch named '...' already exists" instead of an actionable hint.
        Assert.DoesNotContain("already exists", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ── Branch does not exist — must create and switch to it ─────────────────

    [Fact]
    public async Task CreateEpicBranchAsync_BranchDoesNotExist_CreatesAndSwitchesToBranch()
    {
        var sut = new GitManager(_repoPath);

        await sut.CreateEpicBranchAsync("epic-brand-new");

        var branch = await sut.GetCurrentBranchAsync();
        Assert.Equal("epic-brand-new", branch);
    }

    // ── Branch exists, not currently on it, clean working tree — must check out ─

    /// <summary>
    /// Regression test for the v0.1.5 crash: when a branch already exists from a prior run
    /// and the working tree is clean, <see cref="GitManager.CreateEpicBranchAsync"/> must
    /// silently check it out rather than throwing. The old code tried <c>git checkout -b</c>
    /// as a fallback, which git rejects with "a branch named '...' already exists".
    /// </summary>
    [Fact]
    public async Task CreateEpicBranchAsync_BranchExistsAndNotCurrentlyOnIt_ChecksOutSuccessfully()
    {
        // Arrange: create the epic branch and make a commit on it so it has its own history
        RunGit("checkout -b epic-resume");
        File.WriteAllText(Path.Combine(_repoPath, "feature.txt"), "epic work");
        RunGit("add .");
        RunGit("commit -m \"epic commit\"");

        // Switch back to the default branch (clean working tree)
        var defaultBranch = GetDefaultBranch();
        RunGit($"checkout {defaultBranch}");

        var sut = new GitManager(_repoPath);

        // Act — simulates a second run of ralph-loop after the branch was already created
        await sut.CreateEpicBranchAsync("epic-resume");

        // Assert: we are on the epic branch and no exception was thrown
        var branch = await sut.GetCurrentBranchAsync();
        Assert.Equal("epic-resume", branch);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private string GetDefaultBranch()
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = _repoPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("rev-parse");
        psi.ArgumentList.Add("--abbrev-ref");
        psi.ArgumentList.Add("HEAD");
        using var p = Process.Start(psi)!;
        p.WaitForExit();
        return p.StandardOutput.ReadToEnd().Trim();
    }

    private void RunGit(string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = args,
            WorkingDirectory = _repoPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var p = Process.Start(psi)!;
        p.WaitForExit();
    }
}
