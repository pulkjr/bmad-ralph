using RalphLoop.Build;
using Xunit;

namespace RalphLoop.Tests.Build;

public class TestScriptRunnerTests : IDisposable
{
    private readonly string _tempDir;

    public TestScriptRunnerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    // ── Exists ────────────────────────────────────────────────────────────────

    [Fact]
    public void Exists_ReturnsFalse_WhenNoScript()
    {
        var runner = new TestScriptRunner(_tempDir);
        Assert.False(runner.Exists);
    }

    [Fact]
    public void Exists_ReturnsTrue_WhenScriptPresent()
    {
        WriteScript("exit 0");
        var runner = new TestScriptRunner(_tempDir);
        Assert.True(runner.Exists);
    }

    // ── GetMissingScriptPrompt ────────────────────────────────────────────────

    [Fact]
    public void GetMissingScriptPrompt_UxProject_MentionsAgentTui()
    {
        var runner = new TestScriptRunner(_tempDir);
        var prompt = runner.GetMissingScriptPrompt(isUxProject: true);
        Assert.Contains("agent-tui", prompt);
    }

    [Fact]
    public void GetMissingScriptPrompt_StandardProject_MentionsDotnet()
    {
        var runner = new TestScriptRunner(_tempDir);
        var prompt = runner.GetMissingScriptPrompt(isUxProject: false);
        Assert.Contains("dotnet", prompt);
        Assert.DoesNotContain("agent-tui", prompt);
    }

    [Fact]
    public void GetMissingScriptPrompt_BothVariants_IncludeExitZeroInstruction()
    {
        var runner = new TestScriptRunner(_tempDir);
        Assert.Contains("exit 0", runner.GetMissingScriptPrompt(isUxProject: false));
        Assert.Contains("Exit 0", runner.GetMissingScriptPrompt(isUxProject: true));
    }

    // ── RunAsync ─────────────────────────────────────────────────────────────
    // These tests invoke bash and are Unix-only.
    // xunit 2.x has no runtime-skip API; on Windows the guard returns early (test passes trivially).

    [Fact]
    public async Task RunAsync_ReturnsFailed_WhenScriptMissing()
    {
        if (OperatingSystem.IsWindows()) return; // bash unavailable

        var runner = new TestScriptRunner(_tempDir);
        var result = await runner.RunAsync();

        Assert.False(result.Passed);
        Assert.Equal(-1, result.ExitCode);
        Assert.Contains("not found", result.Output);
    }

    [Fact]
    public async Task RunAsync_ReturnsPassed_WhenScriptExitsZero()
    {
        if (OperatingSystem.IsWindows()) return;

        WriteScript("echo 'all good'\nexit 0");
        var runner = new TestScriptRunner(_tempDir);
        var result = await runner.RunAsync();

        Assert.True(result.Passed);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task RunAsync_ReturnsFailed_WhenScriptExitsNonZero()
    {
        if (OperatingSystem.IsWindows()) return;

        WriteScript("echo 'build failed' >&2\nexit 1");
        var runner = new TestScriptRunner(_tempDir);
        var result = await runner.RunAsync();

        Assert.False(result.Passed);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("build failed", result.Output);
    }

    [Fact]
    public async Task RunAsync_CapturesStdout_InOutput()
    {
        if (OperatingSystem.IsWindows()) return;

        WriteScript("echo 'hello stdout'");
        var runner = new TestScriptRunner(_tempDir);
        var result = await runner.RunAsync();

        Assert.Contains("hello stdout", result.Output);
    }

    [Fact]
    public async Task RunAsync_ThrowsOperationCanceled_WhenTokenAlreadyCancelled()
    {
        if (OperatingSystem.IsWindows()) return;

        // A script that would run indefinitely — cancellation must stop it.
        WriteScript("sleep 60");
        var runner = new TestScriptRunner(_tempDir);

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // pre-cancel

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => runner.RunAsync(cts.Token));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void WriteScript(string content)
    {
        var path = Path.Combine(_tempDir, "test.sh");
        File.WriteAllText(path, $"#!/usr/bin/env bash\n{content}\n");
        // Set executable bit using the .NET 7+ API — no external subprocess needed.
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    }
}
