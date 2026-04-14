using Microsoft.Data.Sqlite;
using RalphLoop.Data.Models;

namespace RalphLoop.Data.Repositories;

public class StoryRepository(LedgerDb db)
{
    public async Task<IReadOnlyList<Story>> GetByEpicAsync(long epicId)
    {
        var list = new List<Story>();
        await using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, epic_id, name, description, order_index, status,
                   start_time, end_time, rounds, fail_count, tokens_used, acceptance_criteria
            FROM stories WHERE epic_id = @e ORDER BY order_index, id
            """;
        cmd.Parameters.AddWithValue("@e", epicId);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            list.Add(MapStory(reader));
        return list;
    }

    public async Task<long> InsertAsync(
        long epicId,
        string name,
        string description,
        string acceptanceCriteria,
        int orderIndex
    )
    {
        await using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO stories (epic_id, name, description, acceptance_criteria, order_index, status, start_time)
            VALUES (@e, @n, @d, @ac, @o, 'in_progress', datetime('now'));
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("@e", epicId);
        cmd.Parameters.AddWithValue("@n", name);
        cmd.Parameters.AddWithValue("@d", description);
        cmd.Parameters.AddWithValue("@ac", acceptanceCriteria);
        cmd.Parameters.AddWithValue("@o", orderIndex);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    /// <summary>
    /// Atomically increments rounds and fail_count in a single UPDATE (M1).
    /// Pass false for <paramref name="failed"/> if QA passed.
    /// </summary>
    public async Task IncrementRoundAsync(long storyId, bool failed)
    {
        await using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = failed
            ? "UPDATE stories SET rounds = rounds + 1, fail_count = fail_count + 1 WHERE id = @id"
            : "UPDATE stories SET rounds = rounds + 1 WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", storyId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task UpdateStatusAsync(long storyId, string status)
    {
        if (string.IsNullOrWhiteSpace(status))
            throw new ArgumentException("Story status must not be null or empty.", nameof(status));

        await using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "UPDATE stories SET status = @s WHERE id = @id";
        cmd.Parameters.AddWithValue("@s", status);
        cmd.Parameters.AddWithValue("@id", storyId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task IncrementFailCountAsync(long storyId)
    {
        await using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "UPDATE stories SET fail_count = fail_count + 1 WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", storyId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task AddTokensAsync(long storyId, long tokens)
    {
        await using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "UPDATE stories SET tokens_used = tokens_used + @t WHERE id = @id";
        cmd.Parameters.AddWithValue("@t", tokens);
        cmd.Parameters.AddWithValue("@id", storyId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<int> GetFailCountAsync(long storyId)
    {
        await using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT fail_count FROM stories WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", storyId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    public async Task<int> GetRoundsAsync(long storyId)
    {
        await using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT rounds FROM stories WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", storyId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    public async Task MarkCompleteAsync(long storyId)
    {
        await using var cmd = db.Connection.CreateCommand();
        cmd.CommandText =
            "UPDATE stories SET status = 'complete', end_time = datetime('now') WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", storyId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task AddEventAsync(
        long storyId,
        string eventType,
        string details = "",
        long tokens = 0
    )
    {
        await using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO story_events (story_id, event_type, details, tokens_used)
            VALUES (@s, @e, @d, @t)
            """;
        cmd.Parameters.AddWithValue("@s", storyId);
        cmd.Parameters.AddWithValue("@e", eventType);
        cmd.Parameters.AddWithValue("@d", details);
        cmd.Parameters.AddWithValue("@t", tokens);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task InsertRetrospectiveAsync(long epicId, string notes)
    {
        await using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "INSERT INTO retrospectives (epic_id, notes) VALUES (@e, @n)";
        cmd.Parameters.AddWithValue("@e", epicId);
        cmd.Parameters.AddWithValue("@n", notes);
        await cmd.ExecuteNonQueryAsync();
    }

    private static Story MapStory(SqliteDataReader r) =>
        new()
        {
            Id = r.GetInt64(r.GetOrdinal("id")),
            EpicId = r.GetInt64(r.GetOrdinal("epic_id")),
            Name = r.GetString(r.GetOrdinal("name")),
            Description = r.GetString(r.GetOrdinal("description")),
            OrderIndex = r.GetInt32(r.GetOrdinal("order_index")),
            Status = r.GetString(r.GetOrdinal("status")),
            StartTime = r.IsDBNull(r.GetOrdinal("start_time"))
                ? null
                : DateTime.Parse(
                    r.GetString(r.GetOrdinal("start_time")),
                    System.Globalization.CultureInfo.InvariantCulture
                ),
            EndTime = r.IsDBNull(r.GetOrdinal("end_time"))
                ? null
                : DateTime.Parse(
                    r.GetString(r.GetOrdinal("end_time")),
                    System.Globalization.CultureInfo.InvariantCulture
                ),
            Rounds = r.GetInt32(r.GetOrdinal("rounds")),
            FailCount = r.GetInt32(r.GetOrdinal("fail_count")),
            TokensUsed = r.GetInt64(r.GetOrdinal("tokens_used")),
            AcceptanceCriteria = r.IsDBNull(r.GetOrdinal("acceptance_criteria"))
                ? string.Empty
                : r.GetString(r.GetOrdinal("acceptance_criteria")),
        };
}
