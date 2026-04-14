using System.Diagnostics;
using RalphLoop.Data;
using Xunit;

namespace RalphLoop.Tests.Integration;

/// <summary>
/// Functional tests that run the actual <c>ralph-loop</c> binary via
/// <c>dotnet exec</c> and verify observable behaviour (exit code, file system state).
///
/// These tests locate the DLL via the test assembly's own output directory — the build
/// system copies referenced project outputs alongside the test runner, so
/// <c>typeof(LedgerDb).Assembly.Location</c> resolves to the correct <c>RalphLoop.dll</c>.
/// </summary>
public class ProgramFunctionalTests : IDisposable
{
    private readonly string _tempDir;

    /// <summary>
    /// Absolute path to RalphLoop.dll resolved from the test output directory.
    /// Because the test project references the main project, MSBuild copies its output
    /// (RalphLoop.dll) into the test runner's bin directory.
    /// </summary>
    private static readonly string RalphLoopDll = Path.Combine(
        Path.GetDirectoryName(typeof(LedgerDb).Assembly.Location)!,
        "RalphLoop.dll"
    );

    public ProgramFunctionalTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    // ── --version ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Version_Flag_ExitsZero()
    {
        var (exitCode, stdout, _) = await RunAsync("--version");

        Assert.Equal(0, exitCode);
        Assert.Contains("ralph-loop", stdout, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Version_Flag_DoesNotCreateLedgerFile()
    {
        // Running --version should exit before any database initialisation.
        // The ledger file must NOT appear in the working directory.
        var ledgerPath = Path.Combine(_tempDir, "ledger.db");

        await RunAsync("--version");

        Assert.False(
            File.Exists(ledgerPath),
            $"ledger.db was unexpectedly created at {ledgerPath}"
        );
    }

    // ── --help ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Help_Flag_ExitsZero()
    {
        var (exitCode, _, _) = await RunAsync("--help");

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task Help_Flag_DoesNotCreateLedgerFile()
    {
        var ledgerPath = Path.Combine(_tempDir, "ledger.db");

        await RunAsync("--help");

        Assert.False(
            File.Exists(ledgerPath),
            $"ledger.db was unexpectedly created at {ledgerPath}"
        );
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(
        string argument,
        int timeoutMs = 15_000
    )
    {
        // Run: dotnet exec /path/to/RalphLoop.dll <argument>
        // Working directory is _tempDir so any accidental file creation lands there.
        var psi = new ProcessStartInfo("dotnet", $"exec \"{RalphLoopDll}\" {argument}")
        {
            WorkingDirectory = _tempDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var proc =
            Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start dotnet process.");

        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            // Read output concurrently with waiting for exit to prevent buffer deadlocks.
            var stdoutTask = proc.StandardOutput.ReadToEndAsync(cts.Token);
            var stderrTask = proc.StandardError.ReadToEndAsync(cts.Token);
            await Task.WhenAll(stdoutTask, stderrTask);
            await proc.WaitForExitAsync(cts.Token);
            return (proc.ExitCode, stdoutTask.Result, stderrTask.Result);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            proc.Kill(entireProcessTree: true);
            throw new TimeoutException($"ralph-loop {argument} did not exit within {timeoutMs}ms.");
        }
    }
}
