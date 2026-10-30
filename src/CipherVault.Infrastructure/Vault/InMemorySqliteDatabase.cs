namespace CipherVault.Infrastructure.Vault;

using System.Runtime.InteropServices;
using Microsoft.Data.Sqlite;
using SQLitePCL;

/// <summary>
/// Loads and saves a SQLite database without ever materializing its plaintext
/// representation as a file. SQLite owns the deserialized native buffer for the
/// lifetime of the returned connection.
/// </summary>
internal static class InMemorySqliteDatabase
{
    public static SqliteConnection CreateEmpty()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = ":memory:",
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        }.ToString());

        connection.Open();
        Configure(connection);
        return connection;
    }

    public static SqliteConnection Open(ReadOnlySpan<byte> databaseBytes)
    {
        if (databaseBytes.IsEmpty)
            throw new InvalidDataException("The decrypted SQLite database is empty.");
        if (databaseBytes.Length < 100
            || !databaseBytes[..16].SequenceEqual("SQLite format 3\0"u8))
        {
            throw new InvalidDataException("The decrypted payload is not a SQLite database.");
        }

        SqliteConnection? connection = null;
        IntPtr buffer = IntPtr.Zero;
        bool ownershipTransferred = false;

        try
        {
            connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = ":memory:",
                Mode = SqliteOpenMode.Memory,
                Cache = SqliteCacheMode.Private,
                Pooling = false,
            }.ToString());
            connection.Open();

            buffer = raw.sqlite3_malloc64(databaseBytes.Length);
            if (buffer == IntPtr.Zero)
                throw new OutOfMemoryException("SQLite could not allocate the in-memory vault buffer.");

            unsafe
            {
                fixed (byte* source = databaseBytes)
                {
                    Buffer.MemoryCopy(
                        source,
                        buffer.ToPointer(),
                        databaseBytes.Length,
                        databaseBytes.Length);
                }
            }

            // Vaults created before the in-memory storage migration can retain
            // SQLite's WAL read/write marker in header bytes 18 and 19. A
            // deserialized database has no filename, so SQLite cannot open the
            // corresponding -wal file and reports SQLITE_CANTOPEN (14) on the
            // first PRAGMA/query. The old save path closed/checkpointed SQLite
            // before encrypting the main database, so switching the clean header
            // back to rollback mode is the correct compatibility conversion.
            if (Marshal.ReadByte(buffer, 18) == 2 || Marshal.ReadByte(buffer, 19) == 2)
            {
                Marshal.WriteByte(buffer, 18, 1);
                Marshal.WriteByte(buffer, 19, 1);
            }

            int result = raw.sqlite3_deserialize(
                connection.Handle,
                "main",
                buffer,
                databaseBytes.Length,
                databaseBytes.Length,
                raw.SQLITE_DESERIALIZE_FREEONCLOSE | raw.SQLITE_DESERIALIZE_RESIZEABLE);

            if (result != raw.SQLITE_OK)
                throw new InvalidDataException($"SQLite rejected the decrypted vault database (error {result}).");

            ownershipTransferred = true;
            Configure(connection);
            return connection;
        }
        catch
        {
            connection?.Dispose();
            throw;
        }
        finally
        {
            if (!ownershipTransferred && buffer != IntPtr.Zero)
                raw.sqlite3_free(buffer);
        }
    }

    public static byte[] Serialize(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (connection.State != System.Data.ConnectionState.Open)
            throw new InvalidOperationException("The in-memory vault connection is not open.");

        IntPtr buffer = raw.sqlite3_serialize(connection.Handle, "main", out long size, 0);
        if (buffer == IntPtr.Zero || size <= 0 || size > int.MaxValue)
        {
            if (buffer != IntPtr.Zero)
                raw.sqlite3_free(buffer);
            throw new InvalidDataException("SQLite could not serialize the in-memory vault database.");
        }

        try
        {
            byte[] result = new byte[checked((int)size)];
            Marshal.Copy(buffer, result, 0, result.Length);
            return result;
        }
        finally
        {
            raw.sqlite3_free(buffer);
        }
    }

    private static void Configure(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=MEMORY;
            PRAGMA temp_store=MEMORY;
            PRAGMA foreign_keys=ON;
            """;
        command.ExecuteNonQuery();
    }
}
