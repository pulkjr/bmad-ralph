using Microsoft.Data.Sqlite;

namespace RalphLoop.Data;

/// <summary>
/// Manages the ledger.db SQLite database — schema creation, connection lifecycle.
/// </summary>
public sealed class LedgerDb : IAsyncDisposable, IDisposable
{
    private readonly string _dbPath;
    private SqliteConnection? _connection;

    public LedgerDb(string dbPath)
    {
        _dbPath = dbPath;
    }

    public SqliteConnection Connection
    {
        get
        {
            if (_connection is null)
                throw new InvalidOperationException("Call OpenAsync first.");
            return _connection;
        }
    }

    public async Task OpenAsync()
    {
        _connection = new SqliteConnection($"Data Source={_dbPath}");
        await _connection.OpenAsync();
        await ApplySchemaAsync();
    }

    /// <summary>
    /// Begins a SQLite transaction. Wrap multi-step story state transitions in a transaction
    /// to ensure that partial failures don't leave the database in an inconsistent state.
    /// </summary>
    public async Task<SqliteTransaction> BeginTransactionAsync() =>
        (SqliteTransaction)await _connection!.BeginTransactionAsync();

    private async Task ApplySchemaAsync()
    {
        var sql = """
            PRAGMA journal_mode=WAL;

            CREATE TABLE IF NOT EXISTS sprints (
                id         INTEGER PRIMARY KEY AUTOINCREMENT,
                name       TEXT NOT NULL,
                status     TEXT NOT NULL DEFAULT 'pending',
                created_at TEXT NOT NULL DEFAULT (datetime('now'))
            );

            CREATE TABLE IF NOT EXISTS epics (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                sprint_id   INTEGER NOT NULL REFERENCES sprints(id),
                name        TEXT NOT NULL,
                description TEXT NOT NULL DEFAULT '',
                status      TEXT NOT NULL DEFAULT 'pending',
                branch_name TEXT NOT NULL DEFAULT '',
                start_time  TEXT,
                end_time    TEXT,
                round       INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS stories (
                id                  INTEGER PRIMARY KEY AUTOINCREMENT,
                epic_id             INTEGER NOT NULL REFERENCES epics(id),
                name                TEXT NOT NULL,
                description         TEXT NOT NULL DEFAULT '',
                acceptance_criteria TEXT NOT NULL DEFAULT '',
                order_index         INTEGER NOT NULL DEFAULT 0,
                status              TEXT NOT NULL DEFAULT 'pending',
                start_time          TEXT,
                end_time            TEXT,
                rounds              INTEGER NOT NULL DEFAULT 0,
                fail_count          INTEGER NOT NULL DEFAULT 0,
                tokens_used         INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS story_events (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                story_id    INTEGER NOT NULL REFERENCES stories(id),
                event_type  TEXT NOT NULL,
                timestamp   TEXT NOT NULL DEFAULT (datetime('now')),
                details     TEXT NOT NULL DEFAULT '',
                tokens_used INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS retrospectives (
                id         INTEGER PRIMARY KEY AUTOINCREMENT,
                epic_id    INTEGER NOT NULL REFERENCES epics(id),
                notes      TEXT NOT NULL DEFAULT '',
                created_at TEXT NOT NULL DEFAULT (datetime('now'))
            );

            -- Indexes for FK columns to eliminate scan on joins (M4)
            CREATE INDEX IF NOT EXISTS idx_epics_sprint_id ON epics(sprint_id);
            CREATE INDEX IF NOT EXISTS idx_stories_epic_id ON stories(epic_id);
            CREATE INDEX IF NOT EXISTS idx_story_events_story_id ON story_events(story_id);
            CREATE INDEX IF NOT EXISTS idx_retrospectives_epic_id ON retrospectives(epic_id);

            -- Migration: add acceptance_criteria column if upgrading from an older ledger.db
            -- SQLite silently ignores duplicate-column errors, so we use a separate command for this.
            """;

        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();

        // Idempotent migration for existing databases (ALTER TABLE is not in IF NOT EXISTS)
        await MigrateAsync();
    }

    private async Task MigrateAsync()
    {
        // Add acceptance_criteria if upgrading from pre-M17 ledger.db
        try
        {
            await using var cmd = _connection!.CreateCommand();
            cmd.CommandText = "ALTER TABLE stories ADD COLUMN acceptance_criteria TEXT NOT NULL DEFAULT ''";
            await cmd.ExecuteNonQueryAsync();
        }
        catch (SqliteException ex) when (ex.Message.Contains("duplicate column"))
        {
            // Already migrated — no action needed
        }
    }

    public void Dispose()
    {
        _connection?.Dispose();
        _connection = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
            _connection = null;
        }
    }
}
