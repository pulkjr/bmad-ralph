using RalphLoop.Config;
using Xunit;

namespace RalphLoop.Tests.Config;

public class CopilotLogLevelTests
{
    // ── Valid levels ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("none")]
    [InlineData("error")]
    [InlineData("warning")]
    [InlineData("info")]
    [InlineData("debug")]
    [InlineData("all")]
    [InlineData("default")]
    public void Validate_AcceptsAllValidLevels(string level)
    {
        // Should not throw
        CopilotLogLevel.Validate(level);
    }

    [Fact]
    public void Default_ConstantIsItself_Valid()
    {
        // Ensures Default is kept in sync with ValidLevels
        CopilotLogLevel.Validate(CopilotLogLevel.Default);
    }

    // ── Regression: the specific value that caused the crash ─────────────────

    [Fact]
    public void Validate_Rejects_Warn()
    {
        // "warn" was the value hard-coded in Program.cs that crashed the CLI.
        // The SDK only accepts "warning".
        var ex = Assert.Throws<ArgumentException>(() => CopilotLogLevel.Validate("warn"));
        Assert.Contains("warn", ex.Message);
    }

    // ── Other invalid values ──────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("Warning")]   // case-sensitive
    [InlineData("WARN")]
    [InlineData("verbose")]
    [InlineData("trace")]
    [InlineData("log")]
    [InlineData("off")]
    [InlineData("0")]
    public void Validate_RejectsInvalidLevels(string level)
    {
        Assert.Throws<ArgumentException>(() => CopilotLogLevel.Validate(level));
    }

    // ── Null safety ───────────────────────────────────────────────────────────

    [Fact]
    public void Validate_RejectsNull_WithArgumentException()
    {
        // null is not in ValidLevels; Contains(null) returns false → ArgumentException,
        // not NullReferenceException.
        Assert.Throws<ArgumentException>(() => CopilotLogLevel.Validate(null!));
    }

    // ── All public constants are in ValidLevels ────────────────────────────────

    [Theory]
    [InlineData(CopilotLogLevel.None)]
    [InlineData(CopilotLogLevel.Error)]
    [InlineData(CopilotLogLevel.Warning)]
    [InlineData(CopilotLogLevel.Info)]
    [InlineData(CopilotLogLevel.Debug)]
    [InlineData(CopilotLogLevel.All)]
    public void Validate_AcceptsEveryPublicConstant(string level)
    {
        // Ensures that if a new constant is added to CopilotLogLevel it is also
        // added to ValidLevels. Each constant must round-trip through Validate.
        CopilotLogLevel.Validate(level);
    }
}
