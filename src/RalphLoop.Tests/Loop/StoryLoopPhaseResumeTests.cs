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
}
