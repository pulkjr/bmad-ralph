using RalphLoop.Data.FileStore;
using RalphLoop.Data.Models;
using RalphLoop.Loop.Phases;
using Xunit;

namespace RalphLoop.Tests.Loop;

/// <summary>
/// Unit tests for the YAML→SQLite status mapping helpers in <see cref="SprintPlanningPhase"/>.
/// These helpers are the core logic that ensures Ralph Loop starts from the correct epic
/// when the project is already partially complete.
/// </summary>
public class SprintPlanningPhaseStatusMappingTests
{
    // ── MapYamlStatusToStoryStatus ────────────────────────────────────────────

    [Theory]
    [InlineData("done", StoryStatus.Complete)]
    [InlineData("Done", StoryStatus.Complete)]
    [InlineData("DONE", StoryStatus.Complete)]
    public void MapYamlStatusToStoryStatus_Done_ReturnsComplete(string yaml, string expected)
    {
        Assert.Equal(expected, SprintPlanningPhase.MapYamlStatusToStoryStatus(yaml));
    }

    [Theory]
    [InlineData("backlog")]
    [InlineData("ready-for-dev")]
    [InlineData("in-progress")]
    [InlineData("pending")]
    [InlineData("")]
    [InlineData("unknown-value")]
    public void MapYamlStatusToStoryStatus_NonDone_ReturnsPending(string yaml)
    {
        Assert.Equal(StoryStatus.Pending, SprintPlanningPhase.MapYamlStatusToStoryStatus(yaml));
    }

    // ── MapYamlStatusToEpicStatus ─────────────────────────────────────────────

    [Fact]
    public void MapYamlStatusToEpicStatus_Done_AllStoriesDone_ReturnsComplete()
    {
        var result = SprintPlanningPhase.MapYamlStatusToEpicStatus(
            "done",
            hasNonDoneStories: false
        );

        Assert.Equal(EpicStatus.Complete, result);
    }

    [Fact]
    public void MapYamlStatusToEpicStatus_Done_HasBacklogStory_ReturnsInProgress()
    {
        // Epic is "done" at the epic level but has a backlog child story.
        // Phase 2 (sprint review) already ran; go straight to Phase 3.
        var result = SprintPlanningPhase.MapYamlStatusToEpicStatus("done", hasNonDoneStories: true);

        Assert.Equal(EpicStatus.InProgress, result);
    }

    [Theory]
    [InlineData("in-progress", false)]
    [InlineData("in-progress", true)]
    [InlineData("IN-PROGRESS", false)]
    public void MapYamlStatusToEpicStatus_InProgress_ReturnsInProgress(string yaml, bool hasNonDone)
    {
        Assert.Equal(
            EpicStatus.InProgress,
            SprintPlanningPhase.MapYamlStatusToEpicStatus(yaml, hasNonDone)
        );
    }

    [Theory]
    [InlineData("backlog", false)]
    [InlineData("backlog", true)]
    [InlineData("pending", false)]
    [InlineData("", false)]
    [InlineData("unknown", true)]
    public void MapYamlStatusToEpicStatus_Backlog_ReturnsPending(string yaml, bool hasNonDone)
    {
        Assert.Equal(
            EpicStatus.Pending,
            SprintPlanningPhase.MapYamlStatusToEpicStatus(yaml, hasNonDone)
        );
    }

    // ── BuildEpicNonDoneMap ───────────────────────────────────────────────────

    [Fact]
    public void BuildEpicNonDoneMap_AllStoriesDone_ReturnsFalseForAllEpics()
    {
        var stories = new List<StatusEntry>
        {
            new("5-1-slug", "done", EntryType.Story),
            new("5-2-slug", "done", EntryType.Story),
            new("5-3-slug", "done", EntryType.Story),
        };

        var map = SprintPlanningPhase.BuildEpicNonDoneMap(stories);

        Assert.True(map.TryGetValue("epic-5", out var hasNonDone));
        Assert.False(hasNonDone);
    }

    [Fact]
    public void BuildEpicNonDoneMap_OneStoryBacklog_ReturnsTrueForEpic()
    {
        var stories = new List<StatusEntry>
        {
            new("5-1-slug", "backlog", EntryType.Story),
            new("5-2-slug", "done", EntryType.Story),
            new("5-3-slug", "done", EntryType.Story),
        };

        var map = SprintPlanningPhase.BuildEpicNonDoneMap(stories);

        Assert.True(map.TryGetValue("epic-5", out var hasNonDone));
        Assert.True(hasNonDone);
    }

    [Fact]
    public void BuildEpicNonDoneMap_MultipleEpics_EachEpicTrackedIndependently()
    {
        var stories = new List<StatusEntry>
        {
            new("4-1-slug", "done", EntryType.Story),
            new("4-2-slug", "done", EntryType.Story),
            new("5-1-slug", "backlog", EntryType.Story),
            new("5-2-slug", "done", EntryType.Story),
        };

        var map = SprintPlanningPhase.BuildEpicNonDoneMap(stories);

        Assert.True(map.TryGetValue("epic-4", out var epic4HasNonDone));
        Assert.False(epic4HasNonDone);

        Assert.True(map.TryGetValue("epic-5", out var epic5HasNonDone));
        Assert.True(epic5HasNonDone);
    }

    [Fact]
    public void BuildEpicNonDoneMap_EmptyList_ReturnsEmptyMap()
    {
        var map = SprintPlanningPhase.BuildEpicNonDoneMap([]);

        Assert.Empty(map);
    }

    // ── Real-world scenario from the provided YAML ────────────────────────────

    [Fact]
    public void RealWorldScenario_ProvidedYaml_Epic5HasBacklogStory_MapsToInProgress()
    {
        // Mirrors the actual sprint-status.yaml provided by the user:
        //   epic-5: done
        //   5-1-baseline-configuration-file-parsing-format-a-and-b: backlog
        //   5-2-multi-key-filter-scoping: done
        //   5-3-threshold-resolution-and-configuration-hierarchy: done
        //   5-4-baseline-directory-scanning-and-os-idiomatic-discovery: done
        var stories = new List<StatusEntry>
        {
            new(
                "5-1-baseline-configuration-file-parsing-format-a-and-b",
                "backlog",
                EntryType.Story
            ),
            new("5-2-multi-key-filter-scoping", "done", EntryType.Story),
            new("5-3-threshold-resolution-and-configuration-hierarchy", "done", EntryType.Story),
            new(
                "5-4-baseline-directory-scanning-and-os-idiomatic-discovery",
                "done",
                EntryType.Story
            ),
        };

        var map = SprintPlanningPhase.BuildEpicNonDoneMap(stories);
        var epicStatus = SprintPlanningPhase.MapYamlStatusToEpicStatus(
            "done",
            hasNonDoneStories: map.TryGetValue("epic-5", out var nd) && nd
        );

        Assert.Equal(EpicStatus.InProgress, epicStatus);
    }

    [Fact]
    public void RealWorldScenario_ProvidedYaml_Epics0Through4_MapToComplete()
    {
        // Epics 0–4 all have done stories; epic-level YAML is also "done".
        // They should map to "complete" so the orchestrator skips them.
        var storiesForEpic0 = new List<StatusEntry>
        {
            new("0-1-solution-scaffolding", "done", EntryType.Story),
            new("0-2-architecture-test-foundation", "done", EntryType.Story),
        };

        var map = SprintPlanningPhase.BuildEpicNonDoneMap(storiesForEpic0);
        var epicStatus = SprintPlanningPhase.MapYamlStatusToEpicStatus(
            "done",
            hasNonDoneStories: map.TryGetValue("epic-0", out var nd) && nd
        );

        Assert.Equal(EpicStatus.Complete, epicStatus);
    }

    [Fact]
    public void RealWorldScenario_Story51Backlog_MapsToStoryPending()
    {
        Assert.Equal(
            StoryStatus.Pending,
            SprintPlanningPhase.MapYamlStatusToStoryStatus("backlog")
        );
    }

    [Fact]
    public void RealWorldScenario_Story52Done_MapsToStoryComplete()
    {
        Assert.Equal(StoryStatus.Complete, SprintPlanningPhase.MapYamlStatusToStoryStatus("done"));
    }
}
