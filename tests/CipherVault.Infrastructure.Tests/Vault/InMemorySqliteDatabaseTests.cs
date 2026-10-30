using CipherVault.Infrastructure.Database;
using CipherVault.Infrastructure.Vault;
using FluentAssertions;
using Xunit;

namespace CipherVault.Infrastructure.Tests.Vault;

public sealed class InMemorySqliteDatabaseTests
{
    [Fact]
    public void Open_AcceptsLegacyDatabaseWhoseHeaderStillUsesWalMode()
    {
        byte[] serialized;
        using (var source = InMemorySqliteDatabase.CreateEmpty())
        {
            SchemaMigrator.InitializeSchema(source);
            using var insert = source.CreateCommand();
            insert.CommandText = """
                INSERT INTO entries (id, title)
                VALUES ('legacy-wal-entry', 'Legacy WAL entry');
                """;
            insert.ExecuteNonQuery();
            serialized = InMemorySqliteDatabase.Serialize(source);
        }

        serialized[18] = 2;
        serialized[19] = 2;

        using var reopened = InMemorySqliteDatabase.Open(serialized);
        using var query = reopened.CreateCommand();
        query.CommandText = "SELECT title FROM entries WHERE id = 'legacy-wal-entry'";

        query.ExecuteScalar().Should().Be("Legacy WAL entry");
    }
}
