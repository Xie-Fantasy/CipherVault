using Microsoft.Data.Sqlite;

namespace CipherVault.Infrastructure.Database;

/// <summary>
/// Factory for creating SQLite connections used by CipherVault.
/// Provides an in-memory connection for decrypted vault data with WAL mode and foreign keys enabled.
/// </summary>
public static class SqliteConnectionFactory
{
    /// <summary>
    /// Creates and opens an in-memory SQLite connection with WAL mode and foreign keys enabled.
    /// The caller is responsible for disposing the returned connection.
    /// </summary>
    /// <returns>An open <see cref="SqliteConnection"/> to an in-memory database.</returns>
    public static SqliteConnection CreateInMemoryConnection()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;";
        cmd.ExecuteNonQuery();

        return conn;
    }
}
