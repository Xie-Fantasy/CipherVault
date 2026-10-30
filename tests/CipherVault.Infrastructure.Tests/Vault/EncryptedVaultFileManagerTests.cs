namespace CipherVault.Infrastructure.Tests.Vault;

using System.Security.Cryptography;
using System.Text;
using CipherVault.Core.Exceptions;
using CipherVault.Infrastructure.Crypto;
using CipherVault.Infrastructure.Tests.TestSupport;
using CipherVault.Infrastructure.Vault;
using FluentAssertions;
using Xunit;

public sealed class EncryptedVaultFileManagerTests
{
    [Fact]
    public async Task OpenAndDecrypt_MigratesLegacySchemaBeforeRepositoryUse()
    {
        using var directory = new TemporaryDirectory();
        var crypto = new CryptoService();
        var sut = new EncryptedVaultFileManager(crypto);
        var recovery = new RecoveryKeyGenerator().Generate();
        byte[] password = Encoding.UTF8.GetBytes("legacy-schema-master-password");
        byte[]? vaultKey = null;
        string vaultPath = directory.GetPath("legacy-schema.cvault");

        try
        {
            sut.CreateNew(vaultPath, "Legacy schema", password, recovery.Words);
            var legacyOpen = sut.OpenAndDecrypt(vaultPath, password);
            try
            {
                vaultKey = crypto.DeriveKey(
                    password,
                    legacyOpen.metadata.Salt,
                    legacyOpen.metadata.Argon2MemoryKB,
                    legacyOpen.metadata.Argon2Iterations,
                    legacyOpen.metadata.Argon2Parallelism);

                using (var addLegacyImageColumn = legacyOpen.connection.CreateCommand())
                {
                    addLegacyImageColumn.CommandText = """
                        ALTER TABLE entries ADD COLUMN icon_data BLOB;
                        INSERT INTO entries (id, title, icon_data)
                        VALUES ('legacy-image', 'Legacy image', randomblob(8388608));
                        """;
                    addLegacyImageColumn.ExecuteNonQuery();
                }

                sut.SaveAndEncrypt(vaultPath, legacyOpen.connection, legacyOpen.metadata, vaultKey);
            }
            finally
            {
                legacyOpen.connection.Dispose();
            }

            var migratedOpen = sut.OpenAndDecrypt(vaultPath, password);
            try
            {
                using var repository = new VaultRepository(migratedOpen.connection);
                using (var inspectColumns = migratedOpen.connection.CreateCommand())
                {
                    inspectColumns.CommandText = "PRAGMA table_info(entries)";
                    using var reader = inspectColumns.ExecuteReader();
                    var columns = new List<string>();
                    while (reader.Read())
                        columns.Add(reader.GetString(1));
                    columns.Should().NotContain("icon_data");
                }
                await repository.InsertEntryAsync(new Core.Models.VaultEntry { Title = "Created after migration" });

                (await repository.GetEntriesAsync())
                    .Should()
                    .Contain(entry => entry.Title == "Created after migration");
            }
            finally
            {
                migratedOpen.connection.Dispose();
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(password);
            if (vaultKey != null)
                CryptographicOperations.ZeroMemory(vaultKey);
            Array.Clear(recovery.Words);
        }
    }

    [Fact]
    public void OpenWithRecoveryKey_AcceptsLegacySha256RecoveryBlock()
    {
        using var directory = new TemporaryDirectory();
        var crypto = new CryptoService();
        var sut = new EncryptedVaultFileManager(crypto);
        var recovery = new RecoveryKeyGenerator().Generate();
        byte[] password = Encoding.UTF8.GetBytes("legacy-recovery-master-password");
        string vaultPath = directory.GetPath("legacy-recovery.cvault");
        byte[]? vaultKey = null;

        try
        {
            sut.CreateNew(vaultPath, "Legacy recovery", password, recovery.Words);
            var opened = sut.OpenAndDecrypt(vaultPath, password);
            try
            {
                vaultKey = crypto.DeriveKey(
                    password,
                    opened.metadata.Salt,
                    opened.metadata.Argon2MemoryKB,
                    opened.metadata.Argon2Iterations,
                    opened.metadata.Argon2Parallelism);
                byte[] legacyKey = crypto.DeriveLegacyRecoveryKey(recovery.Words);
                var block = crypto.Encrypt(legacyKey, vaultKey);
                try
                {
                    opened.metadata.RecoveryKeyBlock = block.nonce
                        .Concat(block.ciphertext)
                        .Concat(block.tag)
                        .ToArray();
                    sut.SaveAndEncrypt(vaultPath, opened.connection, opened.metadata, vaultKey);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(legacyKey);
                    CryptographicOperations.ZeroMemory(block.nonce);
                    CryptographicOperations.ZeroMemory(block.ciphertext);
                    CryptographicOperations.ZeroMemory(block.tag);
                }
            }
            finally
            {
                opened.connection.Dispose();
            }

            var recovered = sut.OpenWithRecoveryKey(vaultPath, recovery.Words);
            try
            {
                recovered.vaultKey.Should().Equal(vaultKey);
            }
            finally
            {
                recovered.connection.Dispose();
                CryptographicOperations.ZeroMemory(recovered.vaultKey);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(password);
            if (vaultKey != null)
                CryptographicOperations.ZeroMemory(vaultKey);
            Array.Clear(recovery.Words);
        }
    }

    [Fact]
    public void ResetMasterPassword_RotatesPayloadAndEntryKeysAndKeepsRecoveryWorking()
    {
        using var directory = new TemporaryDirectory();
        var crypto = new CryptoService();
        var sut = new EncryptedVaultFileManager(crypto);
        var recovery = new RecoveryKeyGenerator().Generate();
        byte[] oldPassword = Encoding.UTF8.GetBytes("old-integration-master-password");
        byte[] newPassword = Encoding.UTF8.GetBytes("new-master12");
        byte[] secret = Encoding.UTF8.GetBytes("rotated-entry-secret");
        byte[]? newVaultKey = null;
        string vaultPath = directory.GetPath("reset.cvault");

        try
        {
            sut.CreateNew(vaultPath, "Reset", oldPassword, recovery.Words);
            var recovered = sut.OpenWithRecoveryKey(vaultPath, recovery.Words);
            try
            {
                var encrypted = crypto.Encrypt(recovered.vaultKey, secret);
                byte[] combined = encrypted.nonce.Concat(encrypted.ciphertext).Concat(encrypted.tag).ToArray();
                try
                {
                    using var insert = recovered.connection.CreateCommand();
                    insert.CommandText = "INSERT INTO entries (id, title, password_enc, credential_type) VALUES ('reset-entry', 'Reset', @password, 'login')";
                    insert.Parameters.AddWithValue("@password", combined);
                    insert.ExecuteNonQuery();
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(combined);
                    CryptographicOperations.ZeroMemory(encrypted.ciphertext);
                    CryptographicOperations.ZeroMemory(encrypted.nonce);
                    CryptographicOperations.ZeroMemory(encrypted.tag);
                }

                newVaultKey = sut.ResetMasterPassword(
                    vaultPath,
                    recovered.connection,
                    recovered.metadata,
                    recovered.vaultKey,
                    newPassword,
                    recovery.Words);
            }
            finally
            {
                recovered.connection.Dispose();
                CryptographicOperations.ZeroMemory(recovered.vaultKey);
            }

            Action oldOpen = () => sut.OpenAndDecrypt(vaultPath, oldPassword);
            oldOpen.Should().Throw<InvalidMasterPasswordException>();

            var openedWithNewPassword = sut.OpenAndDecrypt(vaultPath, newPassword);
            try
            {
                byte[] derivedNewKey = crypto.DeriveKey(
                    newPassword,
                    openedWithNewPassword.metadata.Salt,
                    openedWithNewPassword.metadata.Argon2MemoryKB,
                    openedWithNewPassword.metadata.Argon2Iterations,
                    openedWithNewPassword.metadata.Argon2Parallelism);
                try
                {
                    derivedNewKey.Should().Equal(newVaultKey);
                    using var select = openedWithNewPassword.connection.CreateCommand();
                    select.CommandText = "SELECT password_enc FROM entries WHERE id = 'reset-entry'";
                    byte[] blob = (byte[])select.ExecuteScalar()!;
                    byte[] plaintext = EntryPasswordCrypto.Decrypt(
                        crypto, derivedNewKey, "reset-entry", blob[12..^16], blob[..12], blob[^16..]);
                    try { plaintext.Should().Equal(secret); }
                    finally
                    {
                        CryptographicOperations.ZeroMemory(blob);
                        CryptographicOperations.ZeroMemory(plaintext);
                    }
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(derivedNewKey);
                }
            }
            finally
            {
                openedWithNewPassword.connection.Dispose();
            }

            var recoveredAgain = sut.OpenWithRecoveryKey(vaultPath, recovery.Words);
            try { recoveredAgain.vaultKey.Should().Equal(newVaultKey); }
            finally
            {
                recoveredAgain.connection.Dispose();
                CryptographicOperations.ZeroMemory(recoveredAgain.vaultKey);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(oldPassword);
            CryptographicOperations.ZeroMemory(newPassword);
            CryptographicOperations.ZeroMemory(secret);
            if (newVaultKey != null)
                CryptographicOperations.ZeroMemory(newVaultKey);
            Array.Clear(recovery.Words);
        }
    }

    [Fact]
    public void CreateSaveAndRecover_RoundTripsDatabaseAndReturnsActualVaultKey()
    {
        using var directory = new TemporaryDirectory();
        var crypto = new CryptoService();
        var sut = new EncryptedVaultFileManager(crypto);
        var recovery = new RecoveryKeyGenerator().Generate();
        byte[] password = Encoding.UTF8.GetBytes("integration-test-master-password");
        byte[]? expectedVaultKey = null;
        string vaultPath = directory.GetPath("roundtrip.cvault");

        try
        {
            var created = sut.CreateNew(vaultPath, "Round trip", password, recovery.Words);
            created.RecoveryKeyBlock.Should().HaveCount(60);

            var passwordOpen = sut.OpenAndDecryptWithKey(vaultPath, password);
            passwordOpen.connection.DataSource.Should().BeEmpty();
            passwordOpen.connection.ConnectionString.Should().Contain(":memory:");
            byte[] independentlyDerivedKey = crypto.DeriveKey(
                password,
                passwordOpen.metadata.Salt,
                passwordOpen.metadata.Argon2MemoryKB,
                passwordOpen.metadata.Argon2Iterations,
                passwordOpen.metadata.Argon2Parallelism);
            try
            {
                passwordOpen.vaultKey.Should().Equal(independentlyDerivedKey);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(independentlyDerivedKey);
            }
            expectedVaultKey = passwordOpen.vaultKey;

            using (var command = passwordOpen.connection.CreateCommand())
            {
                command.CommandText = "INSERT INTO vault_meta (key, value) VALUES ('roundtrip', 'saved');";
                command.ExecuteNonQuery();
            }

            sut.SaveAndEncrypt(vaultPath, passwordOpen.connection, passwordOpen.metadata, expectedVaultKey);
            passwordOpen.connection.Dispose();

            var recoveryOpen = sut.OpenWithRecoveryKey(vaultPath, recovery.Words);
            try
            {
                recoveryOpen.vaultKey.Should().Equal(expectedVaultKey);
                recoveryOpen.connection.DataSource.Should().BeEmpty();
                recoveryOpen.connection.ConnectionString.Should().Contain(":memory:");

                using var command = recoveryOpen.connection.CreateCommand();
                command.CommandText = "SELECT value FROM vault_meta WHERE key = 'roundtrip';";
                command.ExecuteScalar().Should().Be("saved");
            }
            finally
            {
                recoveryOpen.connection.Dispose();
                CryptographicOperations.ZeroMemory(recoveryOpen.vaultKey);
            }

            Directory.EnumerateFiles(directory.RootPath, "*", SearchOption.AllDirectories)
                .Should()
                .OnlyContain(path =>
                    !path.EndsWith(".db", StringComparison.OrdinalIgnoreCase)
                    && !path.EndsWith(".db-wal", StringComparison.OrdinalIgnoreCase)
                    && !path.EndsWith(".db-shm", StringComparison.OrdinalIgnoreCase),
                    "an unlocked vault must never materialize a plaintext SQLite session on disk");

            string[] wrongWords = (string[])recovery.Words.Clone();
            try
            {
                wrongWords[0] = wrongWords[0] == RecoveryKeyGenerator.DefaultEnglishWordList[0]
                    ? RecoveryKeyGenerator.DefaultEnglishWordList[1]
                    : RecoveryKeyGenerator.DefaultEnglishWordList[0];

                Action wrongRecovery = () => sut.OpenWithRecoveryKey(vaultPath, wrongWords);
                wrongRecovery.Should().Throw<InvalidRecoveryKeyException>();
            }
            finally
            {
                Array.Clear(wrongWords);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(password);
            if (expectedVaultKey != null)
                CryptographicOperations.ZeroMemory(expectedVaultKey);
            Array.Clear(recovery.Words);
        }
    }

    [Fact]
    public void OpenAndDecrypt_WithOutOfRangeKdfHeader_RejectsBeforeKeyDerivation()
    {
        using var directory = new TemporaryDirectory();
        var crypto = new CryptoService();
        var sut = new EncryptedVaultFileManager(crypto);
        var recovery = new RecoveryKeyGenerator().Generate();
        byte[] password = Encoding.UTF8.GetBytes("integration-test-master-password");
        string vaultPath = directory.GetPath("hostile-kdf.cvault");

        try
        {
            sut.CreateNew(vaultPath, "Hostile KDF", password, recovery.Words);

            byte[] fileBytes = File.ReadAllBytes(vaultPath);
            try
            {
                BitConverter.GetBytes(uint.MaxValue).CopyTo(fileBytes, 44);
                byte[] checksum = SHA256.HashData(fileBytes.AsSpan(0, 64));
                try
                {
                    checksum.CopyTo(fileBytes, 68);
                    File.WriteAllBytes(vaultPath, fileBytes);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(checksum);
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(fileBytes);
            }

            Action open = () => sut.OpenAndDecrypt(vaultPath, password);
            open.Should().Throw<VaultCorruptedException>()
                .WithMessage("*Argon2 memory parameter*");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(password);
            Array.Clear(recovery.Words);
        }
    }

}
