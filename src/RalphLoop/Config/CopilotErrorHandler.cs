namespace RalphLoop.Config;

/// <summary>
/// Classifies raw SDK exceptions into user-friendly <see cref="InvalidOperationException"/>s.
///
/// The Copilot SDK surfaces two exception types at the transport/protocol boundary:
/// <list type="bullet">
///   <item><see cref="System.IO.IOException"/> — transport failures (RPC errors, process died,
///   auth denied). Always wraps a <c>StreamJsonRpc.RemoteInvocationException</c> or similar.</item>
///   <item><see cref="InvalidOperationException"/> — protocol errors (CLI not found, client not
///   started, session not found).</item>
/// </list>
///
/// Call <see cref="Rethrow"/> from a catch block to convert either type into a clear,
/// actionable message before it surfaces to the user.
/// </summary>
public static class CopilotErrorHandler
{
    /// <summary>
    /// Inspects <paramref name="ex"/> and rethrows it as an <see cref="InvalidOperationException"/>
    /// with a user-friendly message, or re-throws the original exception unchanged if no
    /// known pattern matches.
    /// </summary>
    /// <exception cref="InvalidOperationException">Always thrown for recognised patterns.</exception>
    public static void Rethrow(Exception ex)
    {
        if (ex is System.IO.IOException ioEx)
        {
            RethrowFromIoException(ioEx);
        }
        else if (ex is InvalidOperationException invEx)
        {
            RethrowFromInvalidOperation(invEx);
        }

        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex).Throw();
    }

    // ── IOException patterns ──────────────────────────────────────────────────

    private static void RethrowFromIoException(System.IO.IOException ex)
    {
        var msg = ex.Message;

        if (
            ContainsIgnoreCase(msg, "Not authenticated")
            || ContainsIgnoreCase(msg, "Please authenticate")
        )
        {
            throw new InvalidOperationException(
                "Not authenticated with GitHub Copilot.\n"
                    + "Run 'gh auth login' to sign in, then try again.",
                ex
            );
        }

        if (ContainsIgnoreCase(msg, "CLI process exited unexpectedly"))
        {
            // The SDK includes stderr output in the message when available.
            throw new InvalidOperationException(
                "The GitHub Copilot CLI process exited unexpectedly.\n"
                    + "Check that 'gh' is installed and the Copilot extension is active.\n"
                    + $"Detail: {msg}",
                ex
            );
        }

        // Generic RPC / communication failure
        throw new InvalidOperationException(
            $"Failed to communicate with GitHub Copilot CLI.\n{msg}",
            ex
        );
    }

    // ── InvalidOperationException patterns ───────────────────────────────────

    private static void RethrowFromInvalidOperation(InvalidOperationException ex)
    {
        var msg = ex.Message;

        if (ContainsIgnoreCase(msg, "Copilot CLI not found"))
        {
            throw new InvalidOperationException(
                "GitHub Copilot CLI was not found.\n"
                    + "Ensure the 'GitHub.Copilot.SDK' NuGet package was fully restored, "
                    + "or install the GitHub CLI with the Copilot extension:\n"
                    + "  gh extension install github/gh-copilot",
                ex
            );
        }

        // Other InvalidOperationExceptions pass through unchanged.
    }

    private static bool ContainsIgnoreCase(string source, string value) =>
        source.Contains(value, StringComparison.OrdinalIgnoreCase);
}
