using System.Diagnostics;
using RalphLoop.Git;
using Xunit;

namespace RalphLoop.Tests.Git;

/// <summary>
/// Security regression tests that verify <see cref="GitManager"/> is not vulnerable to
/// OS command injection via crafted branch/epic/story names.
///
/// Each test passes a real injection payload (semicolon chaining, backtick substitution,
/// $() expansion, etc.) and asserts that a sentinel file was NOT created — proving
/// <see cref="ProcessStartInfo.ArgumentList"/> treats user input as a literal token
/// rather than passing it through a shell for interpretation.
/// </summary>
public sealed class GitManagerInjectionTests : IDisposable
{
    private readonly string _repoPath;
    private readonly string _sentinelPath;

    public GitManagerInjectionTests()
    {
        _repoPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_repoPath);
        _sentinelPath = Path.Combine(_repoPath, "INJECTED.txt");

        // Initialise a bare git repo so git commands have a valid working directory
        RunGit("init");
        RunGit("config user.email test@test.com");
        RunGit("config user.name Test");
        RunGit("commit --allow-empty -m initial");
    }

    public void Dispose() => Directory.Delete(_repoPath, recursive: true);

    // ── CreateEpicBranchAsync injection payloads ──────────────────────────────

    [Theory]
    [InlineData("main; touch INJECTED.txt")]
    [InlineData("main && touch INJECTED.txt")]
    [InlineData("main`touch INJECTED.txt`")]
    [InlineData("$(touch INJECTED.txt)")]
    [InlineData("main\ntouch INJECTED.txt")]
    [InlineData("-b injected; touch INJECTED.txt")]
    [InlineData("''; touch INJECTED.txt; git checkout -b x '")]
    public async Task CreateEpicBranchAsync_WithInjectionPayload_DoesNotCreateSentinelFile(
        string injectionPayload
    )
    {
        var sut = new GitManager(_repoPath);

        // The call may throw (git rejects the malformed branch name) or silently fail —
        // either is acceptable. What must NOT happen is the sentinel file being created.
        try
        {
            await sut.CreateEpicBranchAsync(injectionPayload);
        }
        catch (InvalidOperationException)
        {
            // git rejected the branch name — expected for most payloads
        }

        Assert.False(
            File.Exists(_sentinelPath),
            $"Injection succeeded: sentinel '{_sentinelPath}' was created by payload: {injectionPayload}"
        );
    }

    // ── MergeEpicToMainAsync injection payloads ───────────────────────────────

    [Theory]
    [InlineData("main; touch INJECTED.txt")]
    [InlineData("main --no-ff -m 'msg'; touch INJECTED.txt")]
    [InlineData("HEAD~1; touch INJECTED.txt")]
    public async Task MergeEpicToMainAsync_WithInjectionPayload_DoesNotCreateSentinelFile(
        string injectionPayload
    )
    {
        var sut = new GitManager(_repoPath);

        try
        {
            await sut.MergeEpicToMainAsync(injectionPayload);
        }
        catch (InvalidOperationException)
        {
            // Expected — git will reject the malformed branch reference
        }

        Assert.False(
            File.Exists(_sentinelPath),
            $"Injection succeeded via MergeEpicToMainAsync payload: {injectionPayload}"
        );
    }

    // ── CommitStoryAsync — story/epic names go through stdin, not args ────────

    [Fact]
    public async Task CommitStoryAsync_WithInjectionInStoryName_DoesNotCreateSentinelFile()
    {
        // Stage a file so git commit has something to commit
        var testFile = Path.Combine(_repoPath, "work.txt");
        await File.WriteAllTextAsync(testFile, "content");
        RunGit("add work.txt");

        var sut = new GitManager(_repoPath);

        // Story/epic names end up in the commit *message* passed via stdin, not as shell args.
        // This test confirms that even a malicious story name cannot escape stdin to the shell.
        // CommitStoryAsync returns a result rather than throwing; we ignore the result here
        // since the test only cares that the sentinel was not created.
        _ = await sut.CommitStoryAsync(
            "story; touch INJECTED.txt",
            "epic && touch INJECTED.txt",
            1
        );

        Assert.False(
            File.Exists(_sentinelPath),
            "Injection succeeded via CommitStoryAsync story/epic name"
        );
    }

    // ── FormatCommitMessage — pure static, produces safe output ──────────────

    [Theory]
    [InlineData("story; touch INJECTED.txt", "epic")]
    [InlineData("story", "epic && rm -rf /")]
    [InlineData("$(touch INJECTED.txt)", "epic")]
    public void FormatCommitMessage_WithInjectionInNames_DoesNotExecuteCode(
        string storyName,
        string epicName
    )
    {
        // FormatCommitMessage is pure — it never calls Process.Start.
        // This test verifies it stays that way and produces a message, not a shell command.
        var msg = GitManager.FormatCommitMessage(storyName, epicName, 1);

        Assert.False(
            File.Exists(_sentinelPath),
            "FormatCommitMessage must not execute any process"
        );

        Assert.NotEmpty(msg);
        Assert.Contains("[Intent]:", msg);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

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
