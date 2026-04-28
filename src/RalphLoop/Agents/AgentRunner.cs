using GitHub.Copilot.SDK;
using RalphLoop.UI;

namespace RalphLoop.Agents;

public record AgentResult(string Response, long TokensUsed);

/// <summary>
/// Wraps a Copilot SDK session for a single agent turn.
/// Tracks token usage from AssistantMessageEvents and accumulates tool token costs.
/// Retries transient SessionErrorEvents up to <see cref="MaxRetries"/> times.
/// </summary>
public class AgentRunner(CopilotClient client, ConsoleUI ui, RunLogger runLogger)
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
        CancellationToken ct = default
    )
    {
        Exception? lastException = null;

        ui.ShowAgentIntro(agentLabel, config.Model);

        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            if (attempt > 1)
            {
                var delay = RetryBaseDelay * Math.Pow(2, attempt - 2); // 5s, 10s, 20s
                ui.ShowWarning(
                    $"[{agentLabel}] Retrying (attempt {attempt}/{MaxRetries}) after {delay.TotalSeconds}s..."
                );
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
                ui.ShowWarning(
                    $"[{agentLabel}] Session error (attempt {attempt}/{MaxRetries}): {ex.Message}"
                );
            }
        }

        throw new InvalidOperationException(
            $"[{agentLabel}] Failed after {MaxRetries} attempts.",
            lastException
        );
    }

    private async Task<AgentResult> RunOnceAsync(
        SessionConfig config,
        string prompt,
        string agentLabel,
        CancellationToken ct
    )
    {
        runLogger.LogAgentInput(agentLabel, config.Model, prompt);

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
                    // Note: token counting is handled by AssistantUsageEvent below
                    break;

                case AssistantUsageEvent usage:
                    tokensUsed +=
                        (long)(usage.Data.InputTokens ?? 0) + (long)(usage.Data.OutputTokens ?? 0);
                    break;

                case SessionCompactionStartEvent:
                    ui.ShowInfo($"[{agentLabel}] Context compaction started.");
                    break;

                case SessionCompactionCompleteEvent compact:
                    ui.ShowInfo(
                        $"[{agentLabel}] Context compaction complete: "
                            + $"{compact.Data.PreCompactionTokens} → {compact.Data.PostCompactionTokens} tokens "
                            + $"({(compact.Data.CompactionTokensUsed != null ? (int)(compact.Data.CompactionTokensUsed.Input + compact.Data.CompactionTokensUsed.Output) : 0)} tokens used)."
                    );
                    break;

                case ToolExecutionStartEvent toolStart:
                    runLogger.LogToolEvent(
                        agentLabel,
                        toolStart.Data?.ToolName ?? "(unknown)",
                        "start",
                        ""
                    );
                    break;

                case ToolExecutionCompleteEvent toolComplete:
                    var toolErr = toolComplete.Data?.Error?.Message;
                    if (toolErr is not null)
                        ui.ShowWarning(
                            $"[{agentLabel}] Tool '{toolComplete.Data?.ToolCallId}' failed: {toolErr}"
                        );
                    runLogger.LogToolEvent(
                        agentLabel,
                        toolComplete.Data?.ToolCallId ?? "(unknown)",
                        "complete",
                        toolErr is null ? "ok" : $"error: {toolErr}"
                    );
                    break;

                case ExternalToolRequestedEvent ext:
                    runLogger.LogToolEvent(
                        agentLabel,
                        ext.Data?.ToolName ?? "(unknown)",
                        "external-requested",
                        ""
                    );
                    break;

                case PermissionRequestedEvent perm:
                    var permDetail = System.Text.Json.JsonSerializer.Serialize(
                        perm.Data,
                        new System.Text.Json.JsonSerializerOptions { WriteIndented = false }
                    );
                    runLogger.LogPermissionEvent(agentLabel, "permission.requested", permDetail);
                    break;

                case SessionWarningEvent warn:
                    ui.ShowWarning($"[{agentLabel}] Session warning: {warn.Data?.Message}");
                    break;

                case SessionIdleEvent:
                    if (!done.Task.IsCompleted)
                        done.SetResult();
                    break;

                case SessionErrorEvent err:
                    done.TrySetException(
                        new InvalidOperationException(
                            $"[{agentLabel}] Session error: {err.Data.Message}"
                        )
                    );
                    break;
            }
        });

        await session.SendAsync(new MessageOptions { Prompt = prompt });

        // Wait for idle or cancellation (30-minute hard timeout)
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromMinutes(30));
        await done.Task.WaitAsync(cts.Token);

        var response = responseBuilder.ToString();
        runLogger.LogAgentOutput(agentLabel, tokensUsed, response);
        return new AgentResult(response, tokensUsed);
    }

    /// <summary>
    /// Sends a prompt with an attached file to a new session and waits for idle.
    /// The file is delivered as a <see cref="UserMessageDataAttachmentsItemFile"/> attachment,
    /// avoiding an extra tool-call round-trip for reading the file.
    /// </summary>
    public async Task<AgentResult> RunWithAttachmentAsync(
        SessionConfig config,
        string prompt,
        string attachmentFilePath,
        string agentLabel,
        CancellationToken ct = default
    )
    {
        Exception? lastException = null;

        ui.ShowAgentIntro(agentLabel, config.Model);

        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            if (attempt > 1)
            {
                var delay = RetryBaseDelay * Math.Pow(2, attempt - 2);
                ui.ShowWarning(
                    $"[{agentLabel}] Retrying (attempt {attempt}/{MaxRetries}) after {delay.TotalSeconds}s..."
                );
                await Task.Delay(delay, ct);
            }

            try
            {
                var result = await RunOnceWithAttachmentAsync(
                    config,
                    prompt,
                    attachmentFilePath,
                    agentLabel,
                    ct
                );
                ui.ShowAgentTokenSummary(agentLabel, result.TokensUsed);
                return result;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastException = ex;
                ui.ShowWarning(
                    $"[{agentLabel}] Session error (attempt {attempt}/{MaxRetries}): {ex.Message}"
                );
            }
        }

        throw new InvalidOperationException(
            $"[{agentLabel}] Failed after {MaxRetries} attempts.",
            lastException
        );
    }

    private async Task<AgentResult> RunOnceWithAttachmentAsync(
        SessionConfig config,
        string prompt,
        string attachmentFilePath,
        string agentLabel,
        CancellationToken ct
    )
    {
        runLogger.LogAgentInput(agentLabel, config.Model, prompt);

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
                    break;

                case AssistantUsageEvent usage:
                    tokensUsed +=
                        (long)(usage.Data.InputTokens ?? 0) + (long)(usage.Data.OutputTokens ?? 0);
                    break;

                case SessionCompactionStartEvent:
                    ui.ShowInfo($"[{agentLabel}] Context compaction started.");
                    break;

                case SessionCompactionCompleteEvent compact:
                    ui.ShowInfo(
                        $"[{agentLabel}] Context compaction complete: "
                            + $"{compact.Data.PreCompactionTokens} → {compact.Data.PostCompactionTokens} tokens "
                            + $"({(compact.Data.CompactionTokensUsed != null ? (int)(compact.Data.CompactionTokensUsed.Input + compact.Data.CompactionTokensUsed.Output) : 0)} tokens used)."
                    );
                    break;

                case ToolExecutionStartEvent toolStart:
                    runLogger.LogToolEvent(
                        agentLabel,
                        toolStart.Data?.ToolName ?? "(unknown)",
                        "start",
                        ""
                    );
                    break;

                case ToolExecutionCompleteEvent toolComplete:
                    var toolErr = toolComplete.Data?.Error?.Message;
                    if (toolErr is not null)
                        ui.ShowWarning(
                            $"[{agentLabel}] Tool '{toolComplete.Data?.ToolCallId}' failed: {toolErr}"
                        );
                    runLogger.LogToolEvent(
                        agentLabel,
                        toolComplete.Data?.ToolCallId ?? "(unknown)",
                        "complete",
                        toolErr is null ? "ok" : $"error: {toolErr}"
                    );
                    break;

                case ExternalToolRequestedEvent ext:
                    runLogger.LogToolEvent(
                        agentLabel,
                        ext.Data?.ToolName ?? "(unknown)",
                        "external-requested",
                        ""
                    );
                    break;

                case PermissionRequestedEvent perm:
                    var permDetail = System.Text.Json.JsonSerializer.Serialize(
                        perm.Data,
                        new System.Text.Json.JsonSerializerOptions { WriteIndented = false }
                    );
                    runLogger.LogPermissionEvent(agentLabel, "permission.requested", permDetail);
                    break;

                case SessionWarningEvent warn:
                    ui.ShowWarning($"[{agentLabel}] Session warning: {warn.Data?.Message}");
                    break;

                case SessionIdleEvent:
                    if (!done.Task.IsCompleted)
                        done.SetResult();
                    break;

                case SessionErrorEvent err:
                    done.TrySetException(
                        new InvalidOperationException(
                            $"[{agentLabel}] Session error: {err.Data.Message}"
                        )
                    );
                    break;
            }
        });

        var attachmentItem = new UserMessageDataAttachmentsItemFile
        {
            Path = attachmentFilePath,
            DisplayName = System.IO.Path.GetFileName(attachmentFilePath),
        };
        await session.SendAsync(
            new MessageOptions { Prompt = prompt, Attachments = [attachmentItem] }
        );

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromMinutes(30));
        await done.Task.WaitAsync(cts.Token);

        var response = responseBuilder.ToString();
        runLogger.LogAgentOutput(agentLabel, tokensUsed, response);
        return new AgentResult(response, tokensUsed);
    }

    /// <summary>
    /// Builds the standard permission handler — approves all by default.
    /// </summary>
    public static PermissionRequestHandler ApproveAll() => PermissionHandler.ApproveAll;

    /// <summary>
    /// Builds a permission handler that logs each request to <paramref name="runLogger"/>
    /// before approving it. Use in debug mode to trace what the SDK is asking for.
    /// </summary>
    public static PermissionRequestHandler LoggingApproveAll(
        RunLogger runLogger,
        string agentLabel
    ) =>
        async (request, ct) =>
        {
            var detail = System.Text.Json.JsonSerializer.Serialize(
                request,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = false }
            );
            runLogger.LogPermissionEvent(agentLabel, "approve-all-handler", detail);
            return await PermissionHandler.ApproveAll(request, ct);
        };

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
