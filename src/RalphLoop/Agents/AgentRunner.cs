using GitHub.Copilot.SDK;
using RalphLoop.UI;

namespace RalphLoop.Agents;

public record AgentResult(string Response, long TokensUsed);

/// <summary>
/// Wraps a Copilot SDK session for a single agent turn.
/// Tracks token usage from AssistantMessageEvents and accumulates tool token costs.
/// Retries transient SessionErrorEvents up to <see cref="MaxRetries"/> times.
/// </summary>
public class AgentRunner(CopilotClient client, ConsoleUI ui)
{
    private const int MaxRetries = 3;
    private static readonly TimeSpan RetryBaseDelay = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Sends a prompt to a new session configured by <paramref name="config"/> and waits
    /// for the session to go idle. Returns the accumulated assistant content and total tokens.
    /// Retries up to <see cref="MaxRetries"/> times on transient session errors with backoff.
    /// </summary>
    public async Task<AgentResult> RunAsync(
        SessionConfig config,
        string prompt,
        string agentLabel,
        CancellationToken ct = default)
    {
        Exception? lastException = null;

        ui.ShowAgentIntro(agentLabel, config.Model);

        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            if (attempt > 1)
            {
                var delay = RetryBaseDelay * Math.Pow(2, attempt - 2); // 5s, 10s, 20s
                ui.ShowWarning($"[{agentLabel}] Retrying (attempt {attempt}/{MaxRetries}) after {delay.TotalSeconds}s...");
                await Task.Delay(delay, ct);
            }

            try
            {
                var result = await RunOnceAsync(config, prompt, agentLabel, ct);
                ui.ShowAgentTokenSummary(agentLabel, result.TokensUsed);
                return result;
            }
            catch (OperationCanceledException)
            {
                throw; // Never retry cancellation
            }
            catch (Exception ex)
            {
                lastException = ex;
                ui.ShowWarning($"[{agentLabel}] Session error (attempt {attempt}/{MaxRetries}): {ex.Message}");
            }
        }

        throw new InvalidOperationException(
            $"[{agentLabel}] Failed after {MaxRetries} attempts.", lastException);
    }

    private async Task<AgentResult> RunOnceAsync(
        SessionConfig config,
        string prompt,
        string agentLabel,
        CancellationToken ct)
    {
        await using var session = await client.CreateSessionAsync(config);

        var responseBuilder = new System.Text.StringBuilder();
        long tokensUsed = 0;
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var _ = session.On(evt =>
        {
            switch (evt)
            {
                case AssistantMessageEvent msg:
                    responseBuilder.Append(msg.Data.Content);
                    ui.ShowAgentOutput(agentLabel, msg.Data.Content);
                    tokensUsed += (long)(msg.Data.OutputTokens ?? 0);
                    break;

                case ToolExecutionCompleteEvent:
                    // Tool completions tracked via OutputTokens on AssistantMessageEvent
                    break;

                case SessionIdleEvent:
                    if (!done.Task.IsCompleted)
                        done.SetResult();
                    break;

                case SessionErrorEvent err:
                    done.TrySetException(new InvalidOperationException(
                        $"[{agentLabel}] Session error: {err.Data.Message}"));
                    break;
            }
        });

        await session.SendAsync(new MessageOptions { Prompt = prompt });

        // Wait for idle or cancellation (30-minute hard timeout)
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromMinutes(30));
        await done.Task.WaitAsync(cts.Token);

        return new AgentResult(responseBuilder.ToString(), tokensUsed);
    }

    /// <summary>
    /// Builds the standard permission handler — approves all by default.
    /// </summary>
    public static PermissionRequestHandler ApproveAll() => PermissionHandler.ApproveAll;

    /// <summary>
    /// Builds an OnUserInputRequest handler that delegates to ConsoleUI for terminal prompts.
    /// </summary>
    public UserInputHandler UserInputHandler() =>
        async (req, _) =>
        {
            var answer = await ui.WaitForUserInputAsync(req.Question ?? "Input required:");
            return new UserInputResponse { Answer = answer, WasFreeform = true };
        };
}
