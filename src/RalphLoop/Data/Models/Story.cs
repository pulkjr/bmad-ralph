namespace RalphLoop.Data.Models;

public class Story
{
    public long Id { get; set; }
    public long EpicId { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string AcceptanceCriteria { get; set; } = "";
    public int OrderIndex { get; set; }
    public string Status { get; set; } = StoryStatus.Pending;
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public int Rounds { get; set; } = 0;
    public int FailCount { get; set; } = 0;
    public long TokensUsed { get; set; } = 0;
}

public static class StoryStatus
{
    public const string Pending = "pending";
    public const string InProgress = "in_progress";
    public const string ReadyForReview = "ready_for_review";
    public const string QaPassed = "qa_passed";
    public const string BuildPassed = "build_passed";
    public const string Complete = "complete";
    public const string Failed = "failed";
}
