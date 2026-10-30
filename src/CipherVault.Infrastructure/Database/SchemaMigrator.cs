using Microsoft.Data.Sqlite;

namespace CipherVault.Infrastructure.Database;

/// <summary>
/// Manages database schema initialization and migrations for CipherVault.
/// Executes the initial schema SQL (V001) which creates all required tables and indexes.
/// </summary>
public static class SchemaMigrator
{
    /// <summary>
    /// Initializes the database schema by executing the V001 initial schema SQL.
    /// Creates tables: vault_meta, entries, groups, tags, entry_tags, entry_history,
    /// and all associated indexes.
    /// </summary>
    /// <param name="connection">An open SQLite connection.</param>
    public static bool InitializeSchema(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = InitialSchemaSql;
        cmd.ExecuteNonQuery();

        return RemoveDeprecatedIconData(connection);
    }

    private static bool RemoveDeprecatedIconData(SqliteConnection connection)
    {
        using var inspect = connection.CreateCommand();
        inspect.CommandText = "PRAGMA table_info(entries)";
        using var reader = inspect.ExecuteReader();
        bool hasIconColumn = false;
        while (reader.Read())
        {
            if (reader.GetString(1).Equals("icon_data", StringComparison.OrdinalIgnoreCase))
            {
                hasIconColumn = true;
                break;
            }
        }

        reader.Close();
        if (!hasIconColumn)
            return false;

        // sqlite3_deserialize() gives this connection no backing file name.
        // VACUUM therefore cannot reliably create its temporary database and can
        // fail with SQLITE_CANTOPEN (14) while opening an otherwise valid vault.
        // secure_delete overwrites the removed image values in-place, so the
        // obsolete plaintext bytes are not left in free database pages.
        using (var remove = connection.CreateCommand())
        {
            remove.CommandText = """
                PRAGMA secure_delete=ON;
                ALTER TABLE entries DROP COLUMN icon_data;
                """;
            remove.ExecuteNonQuery();
        }

        return true;
    }

    private const string InitialSchemaSql = """
        -- 保险库元信息
        CREATE TABLE IF NOT EXISTS vault_meta (
            key   TEXT PRIMARY KEY,
            value TEXT NOT NULL
        );

        -- 密码条目
        CREATE TABLE IF NOT EXISTS entries (
            id              TEXT PRIMARY KEY,
            title           TEXT NOT NULL,
            credential_type TEXT NOT NULL DEFAULT 'login',
            username        TEXT,
            password_enc    BLOB,
            url             TEXT,
            notes           TEXT,
            totp_secret     TEXT,
            is_favorite     INTEGER NOT NULL DEFAULT 0,
            group_id        TEXT,
            custom_fields   TEXT,
            created_at      TEXT NOT NULL DEFAULT (datetime('now')),
            modified_at     TEXT NOT NULL DEFAULT (datetime('now')),
            last_accessed_at TEXT,
            deleted_at      TEXT
        );

        -- 分组
        CREATE TABLE IF NOT EXISTS groups (
            id          TEXT PRIMARY KEY,
            name        TEXT NOT NULL,
            parent_id   TEXT,
            sort_order  INTEGER NOT NULL DEFAULT 0,
            icon_name   TEXT,
            FOREIGN KEY (parent_id) REFERENCES groups(id) ON DELETE SET NULL
        );

        -- 标签
        CREATE TABLE IF NOT EXISTS tags (
            id   TEXT PRIMARY KEY,
            name TEXT NOT NULL UNIQUE,
            color TEXT
        );

        -- 条目-标签关联
        CREATE TABLE IF NOT EXISTS entry_tags (
            entry_id TEXT NOT NULL,
            tag_id   TEXT NOT NULL,
            PRIMARY KEY (entry_id, tag_id),
            FOREIGN KEY (entry_id) REFERENCES entries(id) ON DELETE CASCADE,
            FOREIGN KEY (tag_id)   REFERENCES tags(id)   ON DELETE CASCADE
        );

        -- 条目历史
        CREATE TABLE IF NOT EXISTS entry_history (
            id                 TEXT PRIMARY KEY,
            entry_id           TEXT NOT NULL,
            timestamp          TEXT NOT NULL DEFAULT (datetime('now')),
            snapshot_json      TEXT NOT NULL,
            change_description TEXT,
            FOREIGN KEY (entry_id) REFERENCES entries(id) ON DELETE CASCADE
        );

        -- 索引
        CREATE INDEX IF NOT EXISTS idx_entries_title    ON entries(title);
        CREATE INDEX IF NOT EXISTS idx_entries_group    ON entries(group_id);
        CREATE INDEX IF NOT EXISTS idx_entries_favorite ON entries(is_favorite) WHERE is_favorite = 1;
        CREATE INDEX IF NOT EXISTS idx_entries_modified ON entries(modified_at DESC);
        CREATE INDEX IF NOT EXISTS idx_entries_accessed ON entries(last_accessed_at DESC);
        CREATE INDEX IF NOT EXISTS idx_entries_deleted  ON entries(deleted_at) WHERE deleted_at IS NOT NULL;
        CREATE INDEX IF NOT EXISTS idx_entry_history    ON entry_history(entry_id, timestamp DESC);
        CREATE INDEX IF NOT EXISTS idx_groups_parent    ON groups(parent_id);
    """;
}
