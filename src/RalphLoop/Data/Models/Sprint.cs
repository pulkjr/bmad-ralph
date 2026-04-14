namespace RalphLoop.Data.Models;

public class Sprint
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string Status { get; set; } = SprintStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public static class SprintStatus
{
    public const string Pending = "pending";
    public const string Active = "active";
    public const string Complete = "complete";
}
