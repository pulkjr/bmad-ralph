namespace RalphLoop.Data.Models;

public class Epic
{
    public long Id { get; set; }
    public long SprintId { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Status { get; set; } = EpicStatus.Pending;
    public string BranchName { get; set; } = "";
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public int Round { get; set; } = 0;
}

public static class EpicStatus
{
    public const string Pending = "pending";
    public const string InProgress = "in_progress";
    public const string Complete = "complete";
}
