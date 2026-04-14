using System.Diagnostics;
using System.Text.Json;

namespace RalphLoop.Build;

public record AgentTuiResult(bool Passed, string ScreenshotJson, string Details);

/// <summary>
/// Wraps the agent-tui CLI for automated TUI smoke testing of UX stories.
/// Launches the app in a background process, waits for it to render,
/// captures a screenshot, then cleans up.
/// </summary>
public class AgentTuiRunner(string projectPath)
{
    private bool? _isAvailableCache;

    public async Task<AgentTuiResult> SmokeTestAsync(
        string appCommand,
        CancellationToken ct = default)
    {
        // Launch the app in a background shell and capture its PID
        var script = $"{appCommand} & APP_PID=$!; sleep 3; agent-tui screenshot --json; kill $APP_PID 2>/dev/null";
        var result = await RunCommandAsync("bash", $"-c \"{script.Replace("\"", "\\\"")}\"", ct);

        if (result.ExitCode != 0 && string.IsNullOrWhiteSpace(result.StdOut))
            return new AgentTuiResult(false, "", $"agent-tui smoke test failed: {result.StdErr}");

        var screenshotJson = result.StdOut;
        bool passed;
        string details;

        try
        {
            var doc = JsonDocument.Parse(screenshotJson);
            var hasContent = doc.RootElement.TryGetProperty("content", out var content)
                && content.GetString()?.Length > 0;
            passed = hasContent;
            details = passed ? "Screenshot captured successfully" : "Screenshot content is empty";
        }
        catch (JsonException ex)
        {
            passed = false;
            details = $"Screenshot JSON parse error: {ex.Message}";
        }

        return new AgentTuiResult(passed, screenshotJson, details);
    }

    public async Task<bool> IsAvailableAsync()
    {
        if (_isAvailableCache.HasValue)
            return _isAvailableCache.Value;
        var result = await RunCommandAsync("agent-tui", "--version", CancellationToken.None);
        _isAvailableCache = result.ExitCode == 0;
        return _isAvailableCache.Value;
    }

    private async Task<(int ExitCode, string StdOut, string StdErr)> RunCommandAsync(
        string executable, string args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = args,
            WorkingDirectory = projectPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start {executable}");

        var stdOut = await process.StandardOutput.ReadToEndAsync(ct);
        var stdErr = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        return (process.ExitCode, stdOut, stdErr);
    }
}
