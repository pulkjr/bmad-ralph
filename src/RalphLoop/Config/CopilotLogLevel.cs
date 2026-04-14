namespace RalphLoop.Config;

/// <summary>
/// Valid log-level strings accepted by the Copilot SDK CLI's --log-level flag.
/// </summary>
public static class CopilotLogLevel
{
    public const string None = "none";
    public const string Error = "error";
    public const string Warning = "warning";
    public const string Info = "info";
    public const string Debug = "debug";
    public const string All = "all";
    public const string Default = "warning";

    public static readonly IReadOnlySet<string> ValidLevels = new HashSet<string>(
        StringComparer.Ordinal
    )
    {
        None,
        Error,
        Warning,
        Info,
        Debug,
        All,
        "default",
    };

    /// <summary>
    /// Throws <see cref="ArgumentException"/> when <paramref name="level"/> is not a
    /// valid Copilot SDK log-level string.
    /// </summary>
    public static void Validate(string level)
    {
        if (!ValidLevels.Contains(level))
            throw new ArgumentException(
                $"'{level}' is not a valid Copilot log level. "
                    + $"Allowed values: {string.Join(", ", ValidLevels)}",
                nameof(level)
            );
    }
}
