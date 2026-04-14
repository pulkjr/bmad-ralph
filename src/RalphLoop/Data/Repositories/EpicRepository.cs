using Microsoft.Data.Sqlite;
using RalphLoop.Data.Models;

namespace RalphLoop.Data.Repositories;

public class EpicRepository(LedgerDb db)
{
    public async Task<Epic?> GetNextPendingEpicAsync(long sprintId)
    {
        await using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, sprint_id, name, description, status, branch_name, start_time, end_time, round
            FROM epics WHERE sprint_id = @s AND status = 'pending' ORDER BY id LIMIT 1
            """;
        cmd.Parameters.AddWithValue("@s", sprintId);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return MapEpic(reader);
    }

    public async Task<IReadOnlyList<Epic>> GetBySprintAsync(long sprintId)
    {
        var list = new List<Epic>();
        await using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, sprint_id, name, description, status, branch_name, start_time, end_time, round
            FROM epics WHERE sprint_id = @s ORDER BY id
            """;
        cmd.Parameters.AddWithValue("@s", sprintId);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            list.Add(MapEpic(reader));
        return list;
    }

    public async Task<long> InsertAsync(long sprintId, string name, string description)
    {
        await using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO epics (sprint_id, name, description, status)
            VALUES (@s, @n, @d, 'pending');
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("@s", sprintId);
        cmd.Parameters.AddWithValue("@n", name);
        cmd.Parameters.AddWithValue("@d", description);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    public async Task MarkStartedAsync(long epicId, string branchName)
    {
        await using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = """
            UPDATE epics
            SET status = 'in_progress',
                start_time = datetime('now'),
                round = round + 1,
                branch_name = @b
            WHERE id = @id
            """;
        cmd.Parameters.AddWithValue("@b", branchName);
        cmd.Parameters.AddWithValue("@id", epicId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task MarkCompleteAsync(long epicId)
    {
        await using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "UPDATE epics SET status = 'complete', end_time = datetime('now') WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", epicId);
        await cmd.ExecuteNonQueryAsync();
    }

    private static Epic MapEpic(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(r.GetOrdinal("id")),
        SprintId = r.GetInt64(r.GetOrdinal("sprint_id")),
        Name = r.GetString(r.GetOrdinal("name")),
        Description = r.GetString(r.GetOrdinal("description")),
        Status = r.GetString(r.GetOrdinal("status")),
        BranchName = r.GetString(r.GetOrdinal("branch_name")),
        StartTime = r.IsDBNull(r.GetOrdinal("start_time")) ? null
            : DateTime.Parse(r.GetString(r.GetOrdinal("start_time")), System.Globalization.CultureInfo.InvariantCulture),
        EndTime = r.IsDBNull(r.GetOrdinal("end_time")) ? null
            : DateTime.Parse(r.GetString(r.GetOrdinal("end_time")), System.Globalization.CultureInfo.InvariantCulture),
        Round = r.GetInt32(r.GetOrdinal("round")),
    };
}
