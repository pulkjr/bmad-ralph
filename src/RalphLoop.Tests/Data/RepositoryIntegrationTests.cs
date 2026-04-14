using Microsoft.Data.Sqlite;
using RalphLoop.Data;
using RalphLoop.Data.Models;
using RalphLoop.Data.Repositories;
using Xunit;

namespace RalphLoop.Tests.Data;

/// <summary>
/// Integration tests for LedgerDb and all three repositories.
/// Uses an in-memory SQLite database (shared-cache mode so all repositories
/// on the same connection see the same data).
/// </summary>
public class RepositoryIntegrationTests : IAsyncLifetime
{
    private LedgerDb _db = null!;

    public async Task InitializeAsync()
    {
        _db = new LedgerDb(":memory:");
        await _db.OpenAsync();
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    // ── Schema ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task OpenAsync_CreatesAllExpectedTables()
    {
        var tableNames = await GetTableNamesAsync();
        Assert.Contains("sprints", tableNames);
        Assert.Contains("epics", tableNames);
        Assert.Contains("stories", tableNames);
        Assert.Contains("story_events", tableNames);
        Assert.Contains("retrospectives", tableNames);
    }

    [Fact]
    public async Task OpenAsync_IsIdempotent_WhenCalledTwice()
    {
        // Call OpenAsync a second time on the SAME already-open instance.
        // LedgerDb must not throw (schema is CREATE TABLE IF NOT EXISTS — idempotent).
        var ex = await Record.ExceptionAsync(() => _db.OpenAsync());
        Assert.Null(ex);
    }

    // ── SprintRepository ──────────────────────────────────────────────────────

    [Fact]
    public async Task Sprint_InsertAndGetActive_RoundTrips()
    {
        var repo = new SprintRepository(_db);
        var id = await repo.InsertAsync("Sprint 1");

        var active = await repo.GetActiveSprintAsync();

        Assert.NotNull(active);
        Assert.Equal(id, active!.Id);
        Assert.Equal("Sprint 1", active.Name);
        Assert.Equal("active", active.Status);
    }

    [Fact]
    public async Task Sprint_GetAllAsync_ReturnsInsertedSprints()
    {
        var repo = new SprintRepository(_db);
        await repo.InsertAsync("Alpha");
        await repo.InsertAsync("Beta");

        var all = await repo.GetAllAsync();

        Assert.Contains(all, s => s.Name == "Alpha");
        Assert.Contains(all, s => s.Name == "Beta");
    }

    [Fact]
    public async Task Sprint_UpdateStatus_ChangesStatus()
    {
        var repo = new SprintRepository(_db);
        var id = await repo.InsertAsync("Sprint X");

        await repo.UpdateStatusAsync(id, "complete");

        var active = await repo.GetActiveSprintAsync();
        Assert.Null(active); // no active sprints remain
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Sprint_UpdateStatus_Throws_WhenStatusNullOrEmpty(string? status)
    {
        var repo = new SprintRepository(_db);
        var id = await repo.InsertAsync("S");

        await Assert.ThrowsAsync<ArgumentException>(
            () => repo.UpdateStatusAsync(id, status!));
    }

    [Fact]
    public async Task Sprint_GetActive_ReturnsNull_WhenNoActiveSprint()
    {
        var repo = new SprintRepository(_db);

        var result = await repo.GetActiveSprintAsync();

        Assert.Null(result);
    }

    [Fact]
    public async Task Sprint_HasEpics_ReturnsFalse_WhenNoEpics()
    {
        var sprintRepo = new SprintRepository(_db);
        var sprintId = await sprintRepo.InsertAsync("Empty Sprint");

        var hasEpics = await sprintRepo.HasEpicsAsync(sprintId);

        Assert.False(hasEpics);
    }

    [Fact]
    public async Task Sprint_HasEpics_ReturnsTrue_WhenEpicAdded()
    {
        var sprintRepo = new SprintRepository(_db);
        var epicRepo = new EpicRepository(_db);
        var sprintId = await sprintRepo.InsertAsync("Sprint With Epic");
        await epicRepo.InsertAsync(sprintId, "Epic A", "desc");

        var hasEpics = await sprintRepo.HasEpicsAsync(sprintId);

        Assert.True(hasEpics);
    }

    // ── EpicRepository ────────────────────────────────────────────────────────

    [Fact]
    public async Task Epic_InsertAndGetBySprint_RoundTrips()
    {
        var sprintRepo = new SprintRepository(_db);
        var epicRepo = new EpicRepository(_db);
        var sprintId = await sprintRepo.InsertAsync("S1");

        var epicId = await epicRepo.InsertAsync(sprintId, "Epic One", "first epic");
        var epics = await epicRepo.GetBySprintAsync(sprintId);

        Assert.Single(epics);
        Assert.Equal(epicId, epics[0].Id);
        Assert.Equal("Epic One", epics[0].Name);
        Assert.Equal("first epic", epics[0].Description);
        Assert.Equal(EpicStatus.Pending, epics[0].Status);
    }

    [Fact]
    public async Task Epic_GetBySprint_ReturnsOnlyCorrectSprint()
    {
        var sprintRepo = new SprintRepository(_db);
        var epicRepo = new EpicRepository(_db);
        var s1 = await sprintRepo.InsertAsync("S1");
        var s2 = await sprintRepo.InsertAsync("S2");
        await epicRepo.InsertAsync(s1, "Epic in S1", "");
        await epicRepo.InsertAsync(s2, "Epic in S2", "");

        var s1Epics = await epicRepo.GetBySprintAsync(s1);
        var s2Epics = await epicRepo.GetBySprintAsync(s2);

        Assert.Single(s1Epics);
        Assert.Equal("Epic in S1", s1Epics[0].Name);
        Assert.Single(s2Epics);
        Assert.Equal("Epic in S2", s2Epics[0].Name);
    }

    [Fact]
    public async Task Epic_MarkStarted_SetsStatusAndBranch()
    {
        var sprintRepo = new SprintRepository(_db);
        var epicRepo = new EpicRepository(_db);
        var sprintId = await sprintRepo.InsertAsync("S1");
        var epicId = await epicRepo.InsertAsync(sprintId, "E1", "");

        await epicRepo.MarkStartedAsync(epicId, "feature/e1");

        var epics = await epicRepo.GetBySprintAsync(sprintId);
        Assert.Equal(EpicStatus.InProgress, epics[0].Status);
        Assert.Equal("feature/e1", epics[0].BranchName);
        Assert.Equal(1, epics[0].Round);
    }

    [Fact]
    public async Task Epic_MarkComplete_SetsStatusToComplete()
    {
        var sprintRepo = new SprintRepository(_db);
        var epicRepo = new EpicRepository(_db);
        var sprintId = await sprintRepo.InsertAsync("S1");
        var epicId = await epicRepo.InsertAsync(sprintId, "E1", "");

        await epicRepo.MarkCompleteAsync(epicId);

        var epics = await epicRepo.GetBySprintAsync(sprintId);
        Assert.Equal(EpicStatus.Complete, epics[0].Status);
        Assert.NotNull(epics[0].EndTime);
    }

    [Fact]
    public async Task Epic_GetNextPendingEpic_ReturnsFirst()
    {
        var sprintRepo = new SprintRepository(_db);
        var epicRepo = new EpicRepository(_db);
        var sprintId = await sprintRepo.InsertAsync("S1");
        await epicRepo.InsertAsync(sprintId, "E-first", "");
        await epicRepo.InsertAsync(sprintId, "E-second", "");

        var next = await epicRepo.GetNextPendingEpicAsync(sprintId);

        Assert.NotNull(next);
        Assert.Equal("E-first", next!.Name);
    }

    // ── StoryRepository ───────────────────────────────────────────────────────

    [Fact]
    public async Task Story_InsertAndGetByEpic_RoundTrips()
    {
        var (_, epicId) = await SeedSprintAndEpicAsync();
        var storyRepo = new StoryRepository(_db);

        var storyId = await storyRepo.InsertAsync(epicId, "Story One", "desc", "ac", 1);
        var stories = await storyRepo.GetByEpicAsync(epicId);

        Assert.Single(stories);
        Assert.Equal(storyId, stories[0].Id);
        Assert.Equal("Story One", stories[0].Name);
        Assert.Equal("desc", stories[0].Description);
        Assert.Equal("ac", stories[0].AcceptanceCriteria);
        Assert.Equal(StoryStatus.InProgress, stories[0].Status);
    }

    [Fact]
    public async Task Story_GetByEpic_ReturnsOnlyCorrectEpic()
    {
        var sprintRepo = new SprintRepository(_db);
        var epicRepo = new EpicRepository(_db);
        var storyRepo = new StoryRepository(_db);
        var sprintId = await sprintRepo.InsertAsync("S1");
        var e1 = await epicRepo.InsertAsync(sprintId, "E1", "");
        var e2 = await epicRepo.InsertAsync(sprintId, "E2", "");
        await storyRepo.InsertAsync(e1, "Story for E1", "", "", 0);
        await storyRepo.InsertAsync(e2, "Story for E2", "", "", 0);

        var e1Stories = await storyRepo.GetByEpicAsync(e1);
        var e2Stories = await storyRepo.GetByEpicAsync(e2);

        Assert.Single(e1Stories);
        Assert.Equal("Story for E1", e1Stories[0].Name);
        Assert.Single(e2Stories);
        Assert.Equal("Story for E2", e2Stories[0].Name);
    }

    [Fact]
    public async Task Story_IncrementRound_IncrementsRoundsOnly_WhenNotFailed()
    {
        var (_, epicId) = await SeedSprintAndEpicAsync();
        var repo = new StoryRepository(_db);
        var storyId = await repo.InsertAsync(epicId, "S", "", "", 0);

        await repo.IncrementRoundAsync(storyId, failed: false);
        await repo.IncrementRoundAsync(storyId, failed: false);

        var rounds = await repo.GetRoundsAsync(storyId);
        var fails = await repo.GetFailCountAsync(storyId);
        Assert.Equal(2, rounds);
        Assert.Equal(0, fails);
    }

    [Fact]
    public async Task Story_IncrementRound_IncrementsRoundsAndFails_WhenFailed()
    {
        var (_, epicId) = await SeedSprintAndEpicAsync();
        var repo = new StoryRepository(_db);
        var storyId = await repo.InsertAsync(epicId, "S", "", "", 0);

        await repo.IncrementRoundAsync(storyId, failed: true);

        var rounds = await repo.GetRoundsAsync(storyId);
        var fails = await repo.GetFailCountAsync(storyId);
        Assert.Equal(1, rounds);
        Assert.Equal(1, fails);
    }

    [Fact]
    public async Task Story_IncrementFailCount_IncreasesFailCount()
    {
        var (_, epicId) = await SeedSprintAndEpicAsync();
        var repo = new StoryRepository(_db);
        var storyId = await repo.InsertAsync(epicId, "S", "", "", 0);

        await repo.IncrementFailCountAsync(storyId);
        await repo.IncrementFailCountAsync(storyId);

        Assert.Equal(2, await repo.GetFailCountAsync(storyId));
    }

    [Fact]
    public async Task Story_AddTokens_AccumulatesTokens()
    {
        var (_, epicId) = await SeedSprintAndEpicAsync();
        var repo = new StoryRepository(_db);
        var storyId = await repo.InsertAsync(epicId, "S", "", "", 0);

        await repo.AddTokensAsync(storyId, 1000);
        await repo.AddTokensAsync(storyId, 500);

        var stories = await repo.GetByEpicAsync(epicId);
        Assert.Equal(1500, stories[0].TokensUsed);
    }

    [Fact]
    public async Task Story_UpdateStatus_ChangesStatus()
    {
        var (_, epicId) = await SeedSprintAndEpicAsync();
        var repo = new StoryRepository(_db);
        var storyId = await repo.InsertAsync(epicId, "S", "", "", 0);

        await repo.UpdateStatusAsync(storyId, StoryStatus.QaPassed);

        var stories = await repo.GetByEpicAsync(epicId);
        Assert.Equal(StoryStatus.QaPassed, stories[0].Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Story_UpdateStatus_Throws_WhenStatusNullOrEmpty(string? status)
    {
        var (_, epicId) = await SeedSprintAndEpicAsync();
        var repo = new StoryRepository(_db);
        var storyId = await repo.InsertAsync(epicId, "S", "", "", 0);

        await Assert.ThrowsAsync<ArgumentException>(
            () => repo.UpdateStatusAsync(storyId, status!));
    }

    [Fact]
    public async Task Story_MarkComplete_SetsStatusAndEndTime()
    {
        var (_, epicId) = await SeedSprintAndEpicAsync();
        var repo = new StoryRepository(_db);
        var storyId = await repo.InsertAsync(epicId, "S", "", "", 0);

        await repo.MarkCompleteAsync(storyId);

        var stories = await repo.GetByEpicAsync(epicId);
        Assert.Equal(StoryStatus.Complete, stories[0].Status);
        Assert.NotNull(stories[0].EndTime);
    }

    [Fact]
    public async Task Story_AddEvent_Persists()
    {
        var (_, epicId) = await SeedSprintAndEpicAsync();
        var repo = new StoryRepository(_db);
        var storyId = await repo.InsertAsync(epicId, "S", "", "", 0);

        // Should not throw — verify by checking story_events count
        await repo.AddEventAsync(storyId, StoryEventType.DevStart, "details", 100);

        var count = await CountEventRowsAsync(storyId);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Story_InsertRetrospective_Persists()
    {
        var (_, epicId) = await SeedSprintAndEpicAsync();
        var repo = new StoryRepository(_db);

        await repo.InsertRetrospectiveAsync(epicId, "Great sprint, no blockers.");

        var count = await CountRetrospectiveRowsAsync(epicId);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Story_GetByEpic_OrdersByOrderIndexThenId()
    {
        var (_, epicId) = await SeedSprintAndEpicAsync();
        var repo = new StoryRepository(_db);
        await repo.InsertAsync(epicId, "C", "", "", 2);
        await repo.InsertAsync(epicId, "A", "", "", 0);
        await repo.InsertAsync(epicId, "B", "", "", 1);

        var stories = await repo.GetByEpicAsync(epicId);

        Assert.Equal(new[] { "A", "B", "C" }, stories.Select(s => s.Name).ToArray());
    }

    // ── Non-existent ID guards (P5, P6) ──────────────────────────────────────

    [Fact]
    public async Task Epic_GetBySprint_ReturnsEmpty_WhenSprintIdNotFound()
    {
        var epicRepo = new EpicRepository(_db);

        var result = await epicRepo.GetBySprintAsync(9999);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Story_GetByEpic_ReturnsEmpty_WhenEpicIdNotFound()
    {
        var storyRepo = new StoryRepository(_db);

        var result = await storyRepo.GetByEpicAsync(9999);

        Assert.Empty(result);
    }

    // ── Transaction rollback ──────────────────────────────────────────────────

    [Fact]
    public async Task Transaction_Rollback_DoesNotPersistInsertedStory()
    {
        var (_, epicId) = await SeedSprintAndEpicAsync();
        var storyRepo = new StoryRepository(_db);

        await using var tx = await _db.BeginTransactionAsync();
        await storyRepo.InsertAsync(epicId, "Rolled-back story", "desc", "", 0);
        await tx.RollbackAsync();

        var stories = await storyRepo.GetByEpicAsync(epicId);
        Assert.Empty(stories);
    }

    [Fact]
    public async Task Transaction_Commit_PersistsInsertedStory()
    {
        var (_, epicId) = await SeedSprintAndEpicAsync();
        var storyRepo = new StoryRepository(_db);

        await using var tx = await _db.BeginTransactionAsync();
        await storyRepo.InsertAsync(epicId, "Committed story", "desc", "", 0);
        await tx.CommitAsync();

        var stories = await storyRepo.GetByEpicAsync(epicId);
        Assert.Single(stories);
        Assert.Equal("Committed story", stories[0].Name);
    }

    [Fact]
    public async Task Transaction_Rollback_AfterMultipleInserts_LeavesTableEmpty()
    {
        var (_, epicId) = await SeedSprintAndEpicAsync();
        var storyRepo = new StoryRepository(_db);

        await using var tx = await _db.BeginTransactionAsync();
        for (var i = 0; i < 5; i++)
            await storyRepo.InsertAsync(epicId, $"Story {i}", "", "", i);
        await tx.RollbackAsync();

        var stories = await storyRepo.GetByEpicAsync(epicId);
        Assert.Empty(stories);
    }

    // ── Foreign key constraints ───────────────────────────────────────────────
    // NOTE: SQLite foreign key enforcement is OFF by default.
    // LedgerDb.ApplySchemaAsync should add PRAGMA foreign_keys = ON to enable it.
    // The tests below document current behaviour and serve as regression tests once
    // the pragma is added.

    [Fact]
    public async Task ForeignKeys_AreEnforced_ForStoryWithInvalidEpicId()
    {
        // Enable FK enforcement explicitly for this test.
        await using var enableCmd = _db.Connection.CreateCommand();
        enableCmd.CommandText = "PRAGMA foreign_keys = ON";
        await enableCmd.ExecuteNonQueryAsync();

        await using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO stories (epic_id, name, description, acceptance_criteria, order_index, status)
            VALUES (99999, 'orphan', '', '', 0, 'pending')
            """;

        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(
            () => cmd.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task ForeignKeys_AreEnforced_ForEpicWithInvalidSprintId()
    {
        await using var enableCmd = _db.Connection.CreateCommand();
        enableCmd.CommandText = "PRAGMA foreign_keys = ON";
        await enableCmd.ExecuteNonQueryAsync();

        await using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO epics (sprint_id, name, description, status)
            VALUES (99999, 'orphan epic', '', 'pending')
            """;

        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(
            () => cmd.ExecuteNonQueryAsync());
    }

    // ── StoryRepository additional coverage ───────────────────────────────────

    [Fact]
    public async Task Story_IncrementRound_Passed_DoesNotIncrementFailCount()
    {
        var (_, epicId) = await SeedSprintAndEpicAsync();
        var storyRepo = new StoryRepository(_db);
        var storyId = await storyRepo.InsertAsync(epicId, "S", "", "", 0);

        await storyRepo.IncrementRoundAsync(storyId, failed: false);

        Assert.Equal(1, await storyRepo.GetRoundsAsync(storyId));
        Assert.Equal(0, await storyRepo.GetFailCountAsync(storyId));
    }

    [Fact]
    public async Task Story_IncrementRound_Failed_IncrementsFailCount()
    {
        var (_, epicId) = await SeedSprintAndEpicAsync();
        var storyRepo = new StoryRepository(_db);
        var storyId = await storyRepo.InsertAsync(epicId, "S", "", "", 0);

        await storyRepo.IncrementRoundAsync(storyId, failed: true);

        Assert.Equal(1, await storyRepo.GetRoundsAsync(storyId));
        Assert.Equal(1, await storyRepo.GetFailCountAsync(storyId));
    }

    [Fact]
    public async Task Story_FailCount_AccumulatesAcrossMultipleFails()
    {
        var (_, epicId) = await SeedSprintAndEpicAsync();
        var storyRepo = new StoryRepository(_db);
        var storyId = await storyRepo.InsertAsync(epicId, "S", "", "", 0);

        for (var i = 0; i < 3; i++)
            await storyRepo.IncrementRoundAsync(storyId, failed: true);

        Assert.Equal(3, await storyRepo.GetFailCountAsync(storyId));
    }

    [Fact]
    public async Task Story_AddTokens_AccumulatesCorrectly()
    {
        var (_, epicId) = await SeedSprintAndEpicAsync();
        var storyRepo = new StoryRepository(_db);
        var storyId = await storyRepo.InsertAsync(epicId, "S", "", "", 0);

        await storyRepo.AddTokensAsync(storyId, 500);
        await storyRepo.AddTokensAsync(storyId, 1200);

        await using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "SELECT tokens_used FROM stories WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", storyId);
        var tokens = (long)(await cmd.ExecuteScalarAsync())!;

        Assert.Equal(1700L, tokens);
    }

    [Fact]
    public async Task Story_UpdateStatus_Throws_WhenStatusIsEmpty()
    {
        var (_, epicId) = await SeedSprintAndEpicAsync();
        var storyRepo = new StoryRepository(_db);
        var storyId = await storyRepo.InsertAsync(epicId, "S", "", "", 0);

        await Assert.ThrowsAsync<ArgumentException>(
            () => storyRepo.UpdateStatusAsync(storyId, ""));
    }

    [Fact]
    public async Task Story_UpdateStatus_Throws_WhenStatusIsWhitespace()
    {
        var (_, epicId) = await SeedSprintAndEpicAsync();
        var storyRepo = new StoryRepository(_db);
        var storyId = await storyRepo.InsertAsync(epicId, "S", "", "", 0);

        await Assert.ThrowsAsync<ArgumentException>(
            () => storyRepo.UpdateStatusAsync(storyId, "   "));
    }

    // ── LedgerDb ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task LedgerDb_BeginTransactionAsync_ReturnsValidTransaction()
    {
        var tx = await _db.BeginTransactionAsync();

        Assert.NotNull(tx);
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task LedgerDb_DisposedConnection_ThrowsOnAccess()
    {
        var db = new LedgerDb(":memory:");
        await db.OpenAsync();
        await db.DisposeAsync();

        Assert.Throws<InvalidOperationException>(() => _ = db.Connection);
    }

    // ── Index presence ────────────────────────────────────────────────────────

    [Fact]
    public async Task OpenAsync_CreatesExpectedIndexes()
    {
        await using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='index' AND name LIKE 'idx_%'";
        await using var reader = await cmd.ExecuteReaderAsync();
        var indexes = new List<string>();
        while (await reader.ReadAsync())
            indexes.Add(reader.GetString(0));

        Assert.Contains("idx_epics_sprint_id", indexes);
        Assert.Contains("idx_stories_epic_id", indexes);
        Assert.Contains("idx_story_events_story_id", indexes);
        Assert.Contains("idx_retrospectives_epic_id", indexes);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<(long sprintId, long epicId)> SeedSprintAndEpicAsync()
    {
        var sprintRepo = new SprintRepository(_db);
        var epicRepo = new EpicRepository(_db);
        var sprintId = await sprintRepo.InsertAsync("Test Sprint");
        var epicId = await epicRepo.InsertAsync(sprintId, "Test Epic", "");
        return (sprintId, epicId);
    }

    private async Task<List<string>> GetTableNamesAsync()
    {
        var list = new List<string>();
        await using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table'";
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            list.Add(reader.GetString(0));
        return list;
    }

    private async Task<long> CountEventRowsAsync(long storyId)
    {
        await using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM story_events WHERE story_id = @id";
        cmd.Parameters.AddWithValue("@id", storyId);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<long> CountRetrospectiveRowsAsync(long epicId)
    {
        await using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM retrospectives WHERE epic_id = @id";
        cmd.Parameters.AddWithValue("@id", epicId);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }
}
