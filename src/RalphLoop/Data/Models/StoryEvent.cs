namespace RalphLoop.Data.Models;

public class StoryEvent
{
    public long Id { get; set; }
    public long StoryId { get; set; }
    public string EventType { get; set; } = "";
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Details { get; set; } = "";
    public long TokensUsed { get; set; } = 0;
}

public static class StoryEventType
{
    public const string DevStart = "dev_start";
    public const string DevComplete = "dev_complete";
    public const string QaPass = "qa_pass";
    public const string QaFail = "qa_fail";
    public const string BuildPass = "build_pass";
    public const string BuildFail = "build_fail";
    public const string UiSmokeFail = "ui_smoke_fail";
    public const string UiSmokePass = "ui_smoke_pass";
    public const string SwarmStart = "swarm_start";
    public const string SwarmComplete = "swarm_complete";
    public const string Committed = "committed";
}
