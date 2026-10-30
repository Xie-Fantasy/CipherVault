namespace CipherVault.Infrastructure.Vault;

using System.Text.Json;
using CipherVault.Core.Interfaces;
using CipherVault.Core.Models;
using Microsoft.Data.Sqlite;

/// <summary>
/// SQLite-backed implementation of <see cref="IVaultRepository"/>.
/// Operates on an in-memory SQLite database that holds the decrypted vault data.
/// All queries use parameterized statements to prevent SQL injection.
/// </summary>
public sealed class VaultRepository : IVaultRepository, IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly Func<Task>? _onChangedAsync;
    private readonly Func<Task>? _onAccessMetadataChangedAsync;
    private SqliteTransaction? _activeTransaction;
    private bool _hasPendingChangeNotification;
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public VaultRepository(
        SqliteConnection connection,
        Func<Task>? onChangedAsync = null,
        Func<Task>? onAccessMetadataChangedAsync = null)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _onChangedAsync = onChangedAsync;
        _onAccessMetadataChangedAsync = onAccessMetadataChangedAsync;
    }

    public async Task ExecuteTransactionAsync(Func<Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Repository methods such as SetEntryTagsAsync can be composed inside a
        // larger entry-save or bulk-edit transaction.
        if (_activeTransaction != null)
        {
            await operation();
            return;
        }

        using var transaction = _connection.BeginTransaction();
        _activeTransaction = transaction;
        _hasPendingChangeNotification = false;
        bool shouldNotify = false;

        try
        {
            await operation();
            transaction.Commit();
            shouldNotify = _hasPendingChangeNotification;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
        finally
        {
            _activeTransaction = null;
            _hasPendingChangeNotification = false;
        }

        if (shouldNotify && _onChangedAsync != null)
            await _onChangedAsync();
    }

    #region Entries

    public async Task<List<VaultEntry>> GetEntriesAsync(string? groupId = null, bool includeDeleted = false)
    {
        var entries = new List<VaultEntry>();
        using var cmd = CreateCommand();

        if (groupId is not null)
        {
            cmd.CommandText = includeDeleted
                ? "SELECT * FROM entries WHERE group_id = @groupId"
                : "SELECT * FROM entries WHERE group_id = @groupId AND deleted_at IS NULL";
            cmd.Parameters.AddWithValue("@groupId", groupId);
        }
        else
        {
            cmd.CommandText = includeDeleted
                ? "SELECT * FROM entries"
                : "SELECT * FROM entries WHERE deleted_at IS NULL";
        }

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            entries.Add(MapEntry(reader));
        }

        // Load tags for all entries
        await LoadTagsForEntriesAsync(entries);

        return entries;
    }

    public async Task<VaultEntry?> GetEntryByIdAsync(string id)
    {
        ArgumentNullException.ThrowIfNull(id);

        using var cmd = CreateCommand();
        cmd.CommandText = "SELECT * FROM entries WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);

        using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        var entry = MapEntry(reader);
        reader.Close();

        // Load tags for this entry
        await LoadTagsForEntriesAsync(new List<VaultEntry> { entry });

        return entry;
    }

    public async Task<List<VaultEntry>> SearchEntriesAsync(string query)
    {
        var entries = new List<VaultEntry>();

        if (string.IsNullOrWhiteSpace(query))
            return entries;

        using var cmd = CreateCommand();
        cmd.CommandText = """
            SELECT * FROM entries
            WHERE deleted_at IS NULL
              AND (title LIKE @query
                   OR username LIKE @query
                   OR url LIKE @query
                   OR notes LIKE @query)
            ORDER BY is_favorite DESC, modified_at DESC
        """;
        var likePattern = $"%{query}%";
        cmd.Parameters.AddWithValue("@query", likePattern);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            entries.Add(MapEntry(reader));
        }

        await LoadTagsForEntriesAsync(entries);

        return entries;
    }

    public async Task<List<VaultEntry>> GetFavoriteEntriesAsync()
    {
        var entries = new List<VaultEntry>();
        using var cmd = CreateCommand();
        cmd.CommandText = """
            SELECT * FROM entries
            WHERE is_favorite = 1 AND deleted_at IS NULL
            ORDER BY title ASC
        """;

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            entries.Add(MapEntry(reader));
        }

        await LoadTagsForEntriesAsync(entries);

        return entries;
    }

    public async Task<List<VaultEntry>> GetRecentEntriesAsync(int count = 20)
    {
        var entries = new List<VaultEntry>();
        using var cmd = CreateCommand();
        cmd.CommandText = """
            SELECT * FROM entries
            WHERE last_accessed_at IS NOT NULL AND deleted_at IS NULL
            ORDER BY last_accessed_at DESC
            LIMIT @count
        """;
        cmd.Parameters.AddWithValue("@count", count);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            entries.Add(MapEntry(reader));
        }

        await LoadTagsForEntriesAsync(entries);

        return entries;
    }

    public async Task<string> InsertEntryAsync(VaultEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        using var cmd = CreateCommand();
        cmd.CommandText = """
            INSERT INTO entries (id, title, credential_type, username, password_enc, url, notes,
                                 totp_secret, is_favorite, group_id, custom_fields,
                                 created_at, modified_at, last_accessed_at, deleted_at)
            VALUES (@id, @title, @credential_type, @username, @password_enc, @url, @notes,
                    @totp_secret, @is_favorite, @group_id, @custom_fields,
                    @created_at, @modified_at, @last_accessed_at, @deleted_at)
        """;

        BindEntryParameters(cmd, entry);
        await cmd.ExecuteNonQueryAsync();
        await NotifyChangedAsync();

        return entry.Id;
    }

    public async Task UpdateEntryAsync(VaultEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        entry.ModifiedAt = DateTime.UtcNow;

        using var cmd = CreateCommand();
        cmd.CommandText = """
            UPDATE entries SET
                title = @title,
                credential_type = @credential_type,
                username = @username,
                password_enc = @password_enc,
                url = @url,
                notes = @notes,
                totp_secret = @totp_secret,
                is_favorite = @is_favorite,
                group_id = @group_id,
                custom_fields = @custom_fields,
                modified_at = @modified_at,
                last_accessed_at = @last_accessed_at,
                deleted_at = @deleted_at
            WHERE id = @id
        """;

        BindEntryParameters(cmd, entry);
        await cmd.ExecuteNonQueryAsync();
        await NotifyChangedAsync();
    }

    public async Task MarkEntryAccessedAsync(string id, DateTime accessedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        using var cmd = CreateCommand();
        cmd.CommandText = "UPDATE entries SET last_accessed_at = @accessed_at WHERE id = @id AND deleted_at IS NULL";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@accessed_at", accessedAt.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss"));
        await cmd.ExecuteNonQueryAsync();
        await (_onAccessMetadataChangedAsync?.Invoke() ?? NotifyChangedAsync());
    }

    public async Task SoftDeleteEntryAsync(string id)
    {
        ArgumentNullException.ThrowIfNull(id);

        using var cmd = CreateCommand();
        cmd.CommandText = "UPDATE entries SET deleted_at = datetime('now') WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
        await NotifyChangedAsync();
    }

    public async Task RestoreEntryAsync(string id)
    {
        ArgumentNullException.ThrowIfNull(id);

        using var cmd = CreateCommand();
        cmd.CommandText = "UPDATE entries SET deleted_at = NULL, modified_at = datetime('now') WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
        await NotifyChangedAsync();
    }

    public async Task HardDeleteEntryAsync(string id)
    {
        ArgumentNullException.ThrowIfNull(id);

        using var cmd = CreateCommand();
        cmd.CommandText = "DELETE FROM entries WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
        await NotifyChangedAsync();
    }

    public async Task PurgeDeletedEntriesAsync(int olderThanDays = 30)
    {
        using var cmd = CreateCommand();
        cmd.CommandText = """
            DELETE FROM entries
            WHERE deleted_at IS NOT NULL
              AND deleted_at < datetime('now', @daysAgo)
        """;
        cmd.Parameters.AddWithValue("@daysAgo", $"-{olderThanDays} days");
        if (await cmd.ExecuteNonQueryAsync() > 0)
        {
            await NotifyChangedAsync();
        }
    }

    #endregion

    #region Groups

    public async Task<List<VaultGroup>> GetGroupsAsync()
    {
        var groups = new List<VaultGroup>();
        using var cmd = CreateCommand();
        cmd.CommandText = "SELECT * FROM groups ORDER BY sort_order ASC, name ASC";

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            groups.Add(MapGroup(reader));
        }

        return groups;
    }

    public async Task<string> InsertGroupAsync(VaultGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);

        using var cmd = CreateCommand();
        cmd.CommandText = """
            INSERT INTO groups (id, name, parent_id, sort_order, icon_name)
            VALUES (@id, @name, @parent_id, @sort_order, @icon_name)
        """;
        cmd.Parameters.AddWithValue("@id", group.Id);
        cmd.Parameters.AddWithValue("@name", group.Name);
        cmd.Parameters.AddWithValue("@parent_id", (object?)group.ParentId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@sort_order", group.SortOrder);
        cmd.Parameters.AddWithValue("@icon_name", (object?)group.IconName ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync();
        await NotifyChangedAsync();
        return group.Id;
    }

    public async Task UpdateGroupAsync(VaultGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);

        using var cmd = CreateCommand();
        cmd.CommandText = """
            UPDATE groups SET
                name = @name,
                parent_id = @parent_id,
                sort_order = @sort_order,
                icon_name = @icon_name
            WHERE id = @id
        """;
        cmd.Parameters.AddWithValue("@id", group.Id);
        cmd.Parameters.AddWithValue("@name", group.Name);
        cmd.Parameters.AddWithValue("@parent_id", (object?)group.ParentId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@sort_order", group.SortOrder);
        cmd.Parameters.AddWithValue("@icon_name", (object?)group.IconName ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync();
        await NotifyChangedAsync();
    }

    public async Task DeleteGroupAsync(string id)
    {
        ArgumentNullException.ThrowIfNull(id);

        await ExecuteTransactionAsync(async () =>
        {
            // The initial schema intentionally allowed entries without a formal
            // group FK. Clear references explicitly so deleting a group never
            // leaves orphaned IDs in existing or upgraded vaults.
            using (var entriesCommand = CreateCommand())
            {
                entriesCommand.CommandText = "UPDATE entries SET group_id = NULL WHERE group_id = @id";
                entriesCommand.Parameters.AddWithValue("@id", id);
                await entriesCommand.ExecuteNonQueryAsync();
            }

            using (var childCommand = CreateCommand())
            {
                childCommand.CommandText = "UPDATE groups SET parent_id = NULL WHERE parent_id = @id";
                childCommand.Parameters.AddWithValue("@id", id);
                await childCommand.ExecuteNonQueryAsync();
            }

            using (var deleteCommand = CreateCommand())
            {
                deleteCommand.CommandText = "DELETE FROM groups WHERE id = @id";
                deleteCommand.Parameters.AddWithValue("@id", id);
                await deleteCommand.ExecuteNonQueryAsync();
            }

            await NotifyChangedAsync();
        });
    }

    #endregion

    #region Tags

    public async Task<List<Tag>> GetTagsAsync()
    {
        var tags = new List<Tag>();
        using var cmd = CreateCommand();
        cmd.CommandText = "SELECT * FROM tags ORDER BY name ASC";

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tags.Add(MapTag(reader));
        }

        return tags;
    }

    public async Task<List<Tag>> GetTagsForEntryAsync(string entryId)
    {
        ArgumentNullException.ThrowIfNull(entryId);

        var tags = new List<Tag>();
        using var cmd = CreateCommand();
        cmd.CommandText = """
            SELECT t.id, t.name, t.color
            FROM tags t
            INNER JOIN entry_tags et ON t.id = et.tag_id
            WHERE et.entry_id = @entryId
            ORDER BY t.name ASC
        """;
        cmd.Parameters.AddWithValue("@entryId", entryId);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tags.Add(MapTag(reader));
        }

        return tags;
    }

    public async Task<string> InsertTagAsync(Tag tag)
    {
        ArgumentNullException.ThrowIfNull(tag);

        using var cmd = CreateCommand();
        cmd.CommandText = """
            INSERT INTO tags (id, name, color)
            VALUES (@id, @name, @color)
        """;
        cmd.Parameters.AddWithValue("@id", tag.Id);
        cmd.Parameters.AddWithValue("@name", tag.Name);
        cmd.Parameters.AddWithValue("@color", (object?)tag.Color ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync();
        await NotifyChangedAsync();
        return tag.Id;
    }

    public async Task DeleteTagAsync(string id)
    {
        ArgumentNullException.ThrowIfNull(id);

        using var cmd = CreateCommand();
        cmd.CommandText = "DELETE FROM tags WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
        await NotifyChangedAsync();
    }

    public async Task SetEntryTagsAsync(string entryId, IEnumerable<string> tagIds)
    {
        ArgumentNullException.ThrowIfNull(entryId);
        ArgumentNullException.ThrowIfNull(tagIds);

        await ExecuteTransactionAsync(async () =>
        {
            // Remove existing tags
            using (var deleteCmd = CreateCommand())
            {
                deleteCmd.CommandText = "DELETE FROM entry_tags WHERE entry_id = @entryId";
                deleteCmd.Parameters.AddWithValue("@entryId", entryId);
                await deleteCmd.ExecuteNonQueryAsync();
            }

            // Insert new tags
            foreach (var tagId in tagIds)
            {
                using var insertCmd = CreateCommand();
                insertCmd.CommandText = "INSERT OR IGNORE INTO entry_tags (entry_id, tag_id) VALUES (@entryId, @tagId)";
                insertCmd.Parameters.AddWithValue("@entryId", entryId);
                insertCmd.Parameters.AddWithValue("@tagId", tagId);
                await insertCmd.ExecuteNonQueryAsync();
            }

            await NotifyChangedAsync();
        });
    }

    #endregion

    #region History

    public async Task<List<EntryHistory>> GetEntryHistoryAsync(string entryId)
    {
        ArgumentNullException.ThrowIfNull(entryId);

        var history = new List<EntryHistory>();
        using var cmd = CreateCommand();
        cmd.CommandText = """
            SELECT * FROM entry_history
            WHERE entry_id = @entryId
            ORDER BY timestamp DESC
        """;
        cmd.Parameters.AddWithValue("@entryId", entryId);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            history.Add(MapHistory(reader));
        }

        return history;
    }

    public async Task InsertHistoryAsync(EntryHistory history)
    {
        ArgumentNullException.ThrowIfNull(history);

        using var cmd = CreateCommand();
        cmd.CommandText = """
            INSERT INTO entry_history (id, entry_id, timestamp, snapshot_json, change_description)
            VALUES (@id, @entry_id, @timestamp, @snapshot_json, @change_description)
        """;
        cmd.Parameters.AddWithValue("@id", history.Id);
        cmd.Parameters.AddWithValue("@entry_id", history.EntryId);
        cmd.Parameters.AddWithValue("@timestamp", history.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.Parameters.AddWithValue("@snapshot_json", history.SnapshotJson);
        cmd.Parameters.AddWithValue("@change_description", (object?)history.ChangeDescription ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync();
        await NotifyChangedAsync();
    }

    #endregion

    #region Helpers

    private Task NotifyChangedAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_activeTransaction != null)
        {
            _hasPendingChangeNotification = true;
            return Task.CompletedTask;
        }

        return _onChangedAsync?.Invoke() ?? Task.CompletedTask;
    }

    private SqliteCommand CreateCommand()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        SqliteCommand command = _connection.CreateCommand();
        command.Transaction = _activeTransaction;
        return command;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _connection.Dispose();
    }

    private async Task LoadTagsForEntriesAsync(List<VaultEntry> entries)
    {
        if (entries.Count == 0)
            return;

        // Build a comma-separated list of IDs for IN clause
        using var cmd = CreateCommand();
        var idParams = new List<string>();
        for (int i = 0; i < entries.Count; i++)
        {
            var paramName = $"@id{i}";
            idParams.Add(paramName);
            cmd.Parameters.AddWithValue(paramName, entries[i].Id);
        }

        cmd.CommandText = $"""
            SELECT et.entry_id, et.tag_id
            FROM entry_tags et
            WHERE et.entry_id IN ({string.Join(",", idParams)})
        """;

        var tagMap = new Dictionary<string, List<string>>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var entryId = reader.GetString(0);
            var tagId = reader.GetString(1);

            if (!tagMap.TryGetValue(entryId, out var list))
            {
                list = new List<string>();
                tagMap[entryId] = list;
            }
            list.Add(tagId);
        }

        foreach (var entry in entries)
        {
            if (tagMap.TryGetValue(entry.Id, out var tagIds))
            {
                entry.TagIds = tagIds;
            }
        }
    }

    private static VaultEntry MapEntry(SqliteDataReader reader)
    {
        var entry = new VaultEntry
        {
            Id = reader.GetString(reader.GetOrdinal("id")),
            Title = reader.GetString(reader.GetOrdinal("title")),
            CredentialType = ParseCredentialType(reader.GetString(reader.GetOrdinal("credential_type"))),
            Username = GetNullableString(reader, "username"),
            PasswordEncrypted = GetNullableBlob(reader, "password_enc"),
            Url = GetNullableString(reader, "url"),
            Notes = GetNullableString(reader, "notes"),
            TotpSecret = GetNullableString(reader, "totp_secret"),
            IsFavorite = reader.GetInt64(reader.GetOrdinal("is_favorite")) == 1,
            GroupId = GetNullableString(reader, "group_id"),
            CreatedAt = ParseDateTime(reader.GetString(reader.GetOrdinal("created_at"))),
            ModifiedAt = ParseDateTime(reader.GetString(reader.GetOrdinal("modified_at"))),
            LastAccessedAt = GetNullableDateTime(reader, "last_accessed_at"),
            DeletedAt = GetNullableDateTime(reader, "deleted_at"),
        };

        var customFieldsJson = GetNullableString(reader, "custom_fields");
        if (!string.IsNullOrWhiteSpace(customFieldsJson))
        {
            try
            {
                entry.CustomFields = JsonSerializer.Deserialize<List<CustomField>>(customFieldsJson, JsonOptions) ?? new();
            }
            catch (JsonException)
            {
                entry.CustomFields = new();
            }
        }

        return entry;
    }

    private static VaultGroup MapGroup(SqliteDataReader reader)
    {
        return new VaultGroup
        {
            Id = reader.GetString(reader.GetOrdinal("id")),
            Name = reader.GetString(reader.GetOrdinal("name")),
            ParentId = GetNullableString(reader, "parent_id"),
            SortOrder = (int)reader.GetInt64(reader.GetOrdinal("sort_order")),
            IconName = GetNullableString(reader, "icon_name"),
        };
    }

    private static Tag MapTag(SqliteDataReader reader)
    {
        return new Tag
        {
            Id = reader.GetString(reader.GetOrdinal("id")),
            Name = reader.GetString(reader.GetOrdinal("name")),
            Color = GetNullableString(reader, "color"),
        };
    }

    private static EntryHistory MapHistory(SqliteDataReader reader)
    {
        return new EntryHistory
        {
            Id = reader.GetString(reader.GetOrdinal("id")),
            EntryId = reader.GetString(reader.GetOrdinal("entry_id")),
            Timestamp = ParseDateTime(reader.GetString(reader.GetOrdinal("timestamp"))),
            SnapshotJson = reader.GetString(reader.GetOrdinal("snapshot_json")),
            ChangeDescription = GetNullableString(reader, "change_description"),
        };
    }

    private static void BindEntryParameters(SqliteCommand cmd, VaultEntry entry)
    {
        cmd.Parameters.AddWithValue("@id", entry.Id);
        cmd.Parameters.AddWithValue("@title", entry.Title);
        cmd.Parameters.AddWithValue("@credential_type", CredentialTypeToString(entry.CredentialType));
        cmd.Parameters.AddWithValue("@username", (object?)entry.Username ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@password_enc", (object?)entry.PasswordEncrypted ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@url", (object?)entry.Url ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@notes", (object?)entry.Notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@totp_secret", (object?)entry.TotpSecret ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@is_favorite", entry.IsFavorite ? 1 : 0);
        cmd.Parameters.AddWithValue("@group_id", (object?)entry.GroupId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@custom_fields", entry.CustomFields.Count > 0
            ? JsonSerializer.Serialize(entry.CustomFields, JsonOptions)
            : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@created_at", entry.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.Parameters.AddWithValue("@modified_at", entry.ModifiedAt.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.Parameters.AddWithValue("@last_accessed_at", entry.LastAccessedAt.HasValue
            ? entry.LastAccessedAt.Value.ToString("yyyy-MM-dd HH:mm:ss")
            : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@deleted_at", entry.DeletedAt.HasValue
            ? entry.DeletedAt.Value.ToString("yyyy-MM-dd HH:mm:ss")
            : (object)DBNull.Value);
    }

    private static string CredentialTypeToString(CredentialType type) => type switch
    {
        CredentialType.Login => "login",
        CredentialType.CreditCard => "credit_card",
        CredentialType.IdCard => "id_card",
        CredentialType.SecureNote => "secure_note",
        CredentialType.WiFi => "wifi",
        CredentialType.Server => "server",
        CredentialType.Custom => "custom",
        _ => "login"
    };

    private static CredentialType ParseCredentialType(string value) => value.ToLowerInvariant() switch
    {
        "login" => CredentialType.Login,
        "credit_card" => CredentialType.CreditCard,
        "id_card" => CredentialType.IdCard,
        "secure_note" => CredentialType.SecureNote,
        "wifi" => CredentialType.WiFi,
        "server" => CredentialType.Server,
        "custom" => CredentialType.Custom,
        _ => CredentialType.Login
    };

    private static string? GetNullableString(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static byte[]? GetNullableBlob(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : (byte[])reader.GetValue(ordinal);
    }

    private static DateTime? GetNullableDateTime(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        if (reader.IsDBNull(ordinal))
            return null;
        var str = reader.GetString(ordinal);
        return ParseDateTime(str);
    }

    private static DateTime ParseDateTime(string value)
    {
        if (DateTime.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out var dt))
            return dt.ToUniversalTime();
        return DateTime.UtcNow;
    }

    #endregion
}
