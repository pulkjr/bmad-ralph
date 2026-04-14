using Microsoft.Data.Sqlite;
using RalphLoop.Data.Models;

namespace RalphLoop.Data.Repositories;

public class SprintRepository(LedgerDb db)
{
    public async Task<Sprint?> GetActiveSprintAsync()
    {
        await using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT id, name, status, created_at FROM sprints WHERE status = 'active' ORDER BY id DESC LIMIT 1";
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return MapSprint(reader);
    }

    public async Task<IReadOnlyList<Sprint>> GetAllAsync()
    {
        var list = new List<Sprint>();
        await using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT id, name, status, created_at FROM sprints ORDER BY id";
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            list.Add(MapSprint(reader));
        return list;
    }

    public async Task<long> InsertAsync(string name)
    {
        await using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "INSERT INTO sprints (name, status) VALUES (@n, 'active'); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@n", name);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    public async Task UpdateStatusAsync(long id, string status)
    {
        await using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "UPDATE sprints SET status = @s WHERE id = @id";
        cmd.Parameters.AddWithValue("@s", status);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<bool> HasEpicsAsync(long sprintId)
    {
        await using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM epics WHERE sprint_id = @id";
        cmd.Parameters.AddWithValue("@id", sprintId);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync()) > 0;
    }

    private static Sprint MapSprint(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(r.GetOrdinal("id")),
        Name = r.GetString(r.GetOrdinal("name")),
        Status = r.GetString(r.GetOrdinal("status")),
        CreatedAt = DateTime.Parse(r.GetString(r.GetOrdinal("created_at")),
            System.Globalization.CultureInfo.InvariantCulture),
    };
}
