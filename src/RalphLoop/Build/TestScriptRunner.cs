using System.Diagnostics;

namespace RalphLoop.Build;

public record TestResult(bool Passed, string Output, int ExitCode);

/// <summary>
/// Runs test.sh from the project root and validates exit code 0.
/// If test.sh doesn't exist, reports the condition so the developer can write it.
/// </summary>
public class TestScriptRunner(string projectPath)
{
    private readonly string _scriptPath = Path.Combine(projectPath, "test.sh");

    public bool Exists => File.Exists(_scriptPath);

    /// <summary>
    /// Returns a message instructing the developer to create test.sh for the project's language.
    /// </summary>
    public string GetMissingScriptPrompt(bool isUxProject) =>
        isUxProject
            ? """
              test.sh does not exist. Please write it now. It must:
              1. Build the application
              2. Run unit/integration tests
              3. Use agent-tui to validate all key screens from ux-design-specification.md
              4. Exit 0 on full success

              You may NOT modify test.sh once written — only fix the application code.
              """
            : """
              test.sh does not exist. Please write it now for the language/framework of this project.
              For .NET projects: run `dotnet build`, `dotnet test`, and any linters (e.g. roslynator analyze).
              The script must exit 0 on success.

              You may NOT modify test.sh once written — only fix the application code.
              """;

    public async Task<TestResult> RunAsync(CancellationToken ct = default)
    {
        if (!Exists)
            return new TestResult(false, "test.sh not found", -1);

        var psi = new ProcessStartInfo
        {
            FileName = "bash",
            Arguments = "test.sh",
            WorkingDirectory = projectPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start bash");

        var stdOut = await process.StandardOutput.ReadToEndAsync(ct);
        var stdErr = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        var output = string.Join("\n", new[] { stdOut, stdErr }
            .Where(s => !string.IsNullOrWhiteSpace(s)));

        return new TestResult(process.ExitCode == 0, output, process.ExitCode);
    }
}
