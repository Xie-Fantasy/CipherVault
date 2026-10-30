namespace CipherVault.Infrastructure.Tests.Vault;

using CipherVault.Core.Models;
using CipherVault.Infrastructure.Database;
using CipherVault.Infrastructure.Vault;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

public sealed class VaultRepositoryTests
{
    [Fact]
    public async Task EntryCrud_PersistsExpectedStateAndNotifiesAfterEveryMutation()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        SchemaMigrator.InitializeSchema(connection);

        int changeCount = 0;
        using var sut = new VaultRepository(connection, () =>
        {
            changeCount++;
            return Task.CompletedTask;
        });

        var entry = new VaultEntry
        {
            Title = "GitHub",
            Username = "developer@example.com",
            PasswordEncrypted = [1, 2, 3, 4],
            Url = "https://github.com",
            Notes = "primary account",
            IsFavorite = true,
            CustomFields =
            [
                new CustomField
                {
                    Key = "environment",
                    Value = "production",
                    IsSensitive = false,
                },
            ],
        };

        string insertedId = await sut.InsertEntryAsync(entry);
        VaultEntry? inserted = await sut.GetEntryByIdAsync(insertedId);

        inserted.Should().NotBeNull();
        inserted!.Title.Should().Be("GitHub");
        inserted.Username.Should().Be("developer@example.com");
        inserted.PasswordEncrypted.Should().Equal(1, 2, 3, 4);
        inserted.IsFavorite.Should().BeTrue();
        inserted.CustomFields.Should().ContainSingle();
        inserted.CustomFields[0].Value.Should().Be("production");
        changeCount.Should().Be(1);

        entry.Title = "GitHub Work";
        entry.Username = "work@example.com";
        await sut.UpdateEntryAsync(entry);

        VaultEntry? updated = await sut.GetEntryByIdAsync(entry.Id);
        updated.Should().NotBeNull();
        updated!.Title.Should().Be("GitHub Work");
        updated.Username.Should().Be("work@example.com");
        changeCount.Should().Be(2);

        DateTime modifiedBeforeAccess = updated.ModifiedAt;
        DateTime accessedAt = DateTime.UtcNow.AddSeconds(1);
        await sut.MarkEntryAccessedAsync(entry.Id, accessedAt);

        VaultEntry? accessed = await sut.GetEntryByIdAsync(entry.Id);
        accessed.Should().NotBeNull();
        accessed!.ModifiedAt.Should().Be(modifiedBeforeAccess);
        accessed.LastAccessedAt.Should().NotBeNull();
        (await sut.GetRecentEntriesAsync(1)).Should().ContainSingle(item => item.Id == entry.Id);
        changeCount.Should().Be(3);

        await sut.SoftDeleteEntryAsync(entry.Id);

        (await sut.GetEntriesAsync()).Should().BeEmpty();
        List<VaultEntry> deletedEntries = await sut.GetEntriesAsync(includeDeleted: true);
        deletedEntries.Should().ContainSingle();
        deletedEntries[0].DeletedAt.Should().NotBeNull();
        changeCount.Should().Be(4);

        await sut.RestoreEntryAsync(entry.Id);

        (await sut.GetEntriesAsync()).Should().ContainSingle(item => item.Id == entry.Id);
        changeCount.Should().Be(5);

        await sut.SoftDeleteEntryAsync(entry.Id);

        await sut.HardDeleteEntryAsync(entry.Id);

        (await sut.GetEntryByIdAsync(entry.Id)).Should().BeNull();
        changeCount.Should().Be(7);
    }

    [Fact]
    public void InitializeSchema_RemovesDeprecatedIconDataFromExistingVault()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        using (var create = connection.CreateCommand())
        {
            create.CommandText = """
                CREATE TABLE entries (
                    id TEXT PRIMARY KEY,
                    title TEXT NOT NULL,
                    icon_data BLOB,
                    group_id TEXT,
                    is_favorite INTEGER NOT NULL DEFAULT 0,
                    modified_at TEXT NOT NULL DEFAULT (datetime('now')),
                    last_accessed_at TEXT,
                    deleted_at TEXT
                );
                INSERT INTO entries (id, title, icon_data)
                VALUES ('legacy', 'Legacy image', X'01020304');
                """;
            create.ExecuteNonQuery();
        }

        SchemaMigrator.InitializeSchema(connection);

        using var inspect = connection.CreateCommand();
        inspect.CommandText = "PRAGMA table_info(entries)";
        using var reader = inspect.ExecuteReader();
        var columns = new List<string>();
        while (reader.Read())
            columns.Add(reader.GetString(1));

        columns.Should().NotContain("icon_data");
    }

    [Fact]
    public async Task ExecuteTransaction_CoalescesMultipleMutationsIntoOneNotification()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        SchemaMigrator.InitializeSchema(connection);

        int changeCount = 0;
        using var sut = new VaultRepository(connection, () =>
        {
            changeCount++;
            return Task.CompletedTask;
        });

        await sut.ExecuteTransactionAsync(async () =>
        {
            await sut.InsertEntryAsync(new VaultEntry { Title = "One" });
            await sut.InsertEntryAsync(new VaultEntry { Title = "Two" });
        });

        changeCount.Should().Be(1);
        (await sut.GetEntriesAsync()).Should().HaveCount(2);
    }

    [Fact]
    public async Task ExecuteTransaction_InsertsFiveHundredEntriesWithOnePersistenceNotification()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        SchemaMigrator.InitializeSchema(connection);

        int changeCount = 0;
        using var sut = new VaultRepository(connection, () =>
        {
            changeCount++;
            return Task.CompletedTask;
        });

        await sut.ExecuteTransactionAsync(async () =>
        {
            for (int index = 0; index < 500; index++)
            {
                await sut.InsertEntryAsync(new VaultEntry
                {
                    Title = $"Account {index:000}",
                    Username = $"user{index}@example.test",
                });
            }
        });

        changeCount.Should().Be(1);
        (await sut.GetEntriesAsync()).Should().HaveCount(500);
    }
}
