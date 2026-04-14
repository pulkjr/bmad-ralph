namespace RalphLoop.Data.Models;

public class Retrospective
{
    public long Id { get; set; }
    public long EpicId { get; set; }
    public string Notes { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
