using System.Text.Json;
using RalphLoop.Config;

namespace RalphLoop.UI;

/// <summary>
/// Writes a JSON Lines (.jsonl) debug log of every agent call, phase transition,
/// vote parse result, and error during a run. Enabled via <c>debugLog: true</c>
/// in <c>ralph-loop.json</c>.
///
/// Each line in the log file is a self-contained JSON object with at minimum an
/// <c>event</c> discriminator and an ISO-8601 <c>timestamp</c>. The file is
/// created once per process invocation under <c>&lt;projectPath&gt;/logs/</c>.
/// </summary>
public sealed class RunLogger
{
    private readonly bool _enabled;
    private readonly string? _logPath;
    private readonly object _lock = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public RunLogger(RalphLoopConfig config)
    {
        _enabled = config.DebugLog;
        if (!_enabled)
            return;

        var logsDir = Path.Combine(config.ProjectPath, "logs");
        Directory.CreateDirectory(logsDir);

        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        _logPath = Path.Combine(logsDir, $"ralph-loop-{stamp}.jsonl");
    }

    public void LogPhaseStart(string phase, string description)
    {
        if (!_enabled)
            return;
        Append(
            new
            {
                @event = "phase_start",
                timestamp = Ts(),
                phase,
                description,
            }
        );
    }

    public void LogAgentInput(string agentLabel, string? model, string prompt)
    {
        if (!_enabled)
            return;
        Append(
            new
            {
                @event = "agent_input",
                timestamp = Ts(),
                agent = agentLabel,
                model = model ?? "",
                prompt,
            }
        );
    }

    public void LogAgentOutput(string agentLabel, long tokensUsed, string response)
    {
        if (!_enabled)
            return;
        Append(
            new
            {
                @event = "agent_output",
                timestamp = Ts(),
                agent = agentLabel,
                tokensUsed,
                response,
            }
        );
    }

    public void LogVoteResult(
        int yesCount,
        int noMinorCount,
        int noMajorCount,
        string outcome,
        string rawResponse
    )
    {
        if (!_enabled)
            return;
        Append(
            new
            {
                @event = "vote_result",
                timestamp = Ts(),
                yesCount,
                noMinorCount,
                noMajorCount,
                outcome,
                rawResponse,
            }
        );
    }

    public void LogError(string context, string message)
    {
        if (!_enabled)
            return;
        Append(
            new
            {
                @event = "error",
                timestamp = Ts(),
                context,
                message,
            }
        );
    }

    private static string Ts() => DateTime.UtcNow.ToString("o");

    private void Append(object payload)
    {
        var line = JsonSerializer.Serialize(payload, JsonOptions);
        lock (_lock)
        {
            File.AppendAllText(_logPath!, line + Environment.NewLine);
        }
    }
}
