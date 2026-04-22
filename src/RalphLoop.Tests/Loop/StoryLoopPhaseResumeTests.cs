using RalphLoop.Data.Models;
using RalphLoop.Loop.Phases;
using Xunit;

namespace RalphLoop.Tests.Loop;

public class StoryLoopPhaseResumeTests
{
    [Theory]
    [InlineData(StoryStatus.ReadyForReview, null, 1)]
    [InlineData(StoryStatus.QaPassed, null, 2)]
    [InlineData(StoryStatus.BuildPassed, null, 3)]
    [InlineData(StoryStatus.Pending, null, 0)]
    [InlineData(StoryStatus.InProgress, StoryEventType.DevComplete, 1)]
    [InlineData(StoryStatus.InProgress, StoryEventType.QaPass, 2)]
    [InlineData(StoryStatus.InProgress, StoryEventType.BuildPass, 3)]
    [InlineData(StoryStatus.InProgress, StoryEventType.DevStart, 0)]
    [InlineData("", StoryEventType.DevComplete, 1)]
    public void DetermineResumeStep_MapsStatusAndEvent(
        string status,
        string? latestEventType,
        int expected
    )
    {
        var step = StoryLoopPhase.DetermineResumeStep(status, latestEventType);
        Assert.Equal((StoryLoopPhase.StoryResumeStep)expected, step);
    }

    // ── ExtractVerdict ────────────────────────────────────────────────────────

    [Fact]
    public void ExtractVerdict_WithVerdictLine_ReturnsValueAfterColon()
    {
        var response = "Some analysis.\nVERDICT: PASS — all tests green";

        var verdict = StoryLoopPhase.ExtractVerdict(response);

        Assert.Equal("PASS — all tests green", verdict);
    }

    [Fact]
    public void ExtractVerdict_NoVerdictLine_ReturnsNull()
    {
        var response = "Everything looks fine. No issues.";

        var verdict = StoryLoopPhase.ExtractVerdict(response);

        Assert.Null(verdict);
    }

    [Fact]
    public void ExtractVerdict_MultipleVerdictLines_ReturnsLast()
    {
        var response = """
            VERDICT: FAIL — first attempt
            Fixed the issues.
            VERDICT: PASS — resolved
            """;

        var verdict = StoryLoopPhase.ExtractVerdict(response);

        Assert.Equal("PASS — resolved", verdict);
    }

    [Fact]
    public void ExtractVerdict_BlockquotePrefixedLine_ReturnsVerdictText()
    {
        // ExtractVerdict now strips "> " blockquote prefix (emitted by the Copilot SDK)
        // and "**" bold markers before checking for the VERDICT: keyword.
        var response = "> VERDICT: RESOLVED — all findings addressed";

        var verdict = StoryLoopPhase.ExtractVerdict(response);

        Assert.NotNull(verdict);
        Assert.StartsWith("RESOLVED", verdict, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExtractVerdict_CaseInsensitive_ReturnsValue()
    {
        var response = "verdict: pass — lowercase verdict";

        var verdict = StoryLoopPhase.ExtractVerdict(response);

        Assert.Equal("pass — lowercase verdict", verdict);
    }
}
