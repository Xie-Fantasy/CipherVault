namespace CipherVault.Infrastructure.Tests;

using System.Security.Cryptography;
using System.Text;
using CipherVault.Core.Models;
using CipherVault.Infrastructure.Backup;
using CipherVault.Infrastructure.Crypto;
using CipherVault.Infrastructure.Tests.TestSupport;
using CipherVault.Infrastructure.Vault;
using FluentAssertions;
using Xunit;

public sealed class EndToEndVaultLifecycleTests
{
    [Fact]
    public async Task CreatePersistReopenAndRestoreBackup_PreservesEncryptedDomainData()
    {
        using var directory = new TemporaryDirectory();
        var crypto = new CryptoService();
        var fileManager = new EncryptedVaultFileManager(crypto);
        var recovery = new RecoveryKeyGenerator().Generate();
        byte[] password = Encoding.UTF8.GetBytes("end-to-end-master-password");
        string vaultPath = directory.GetPath("lifecycle.cvault");
        string restoredPath = directory.GetPath("restored.cvault");
        string backupDirectory = directory.GetPath("backups");
        byte[]? vaultKey = null;
        byte[]? passwordPlaintext = null;

        try
        {
            fileManager.CreateNew(vaultPath, "Lifecycle", password, recovery.Words);
            var opened = fileManager.OpenAndDecrypt(vaultPath, password);
            try
            {
                vaultKey = crypto.DeriveKey(
                    password,
                    opened.metadata.Salt,
                    opened.metadata.Argon2MemoryKB,
                    opened.metadata.Argon2Iterations,
                    opened.metadata.Argon2Parallelism);

                using var repository = new VaultRepository(opened.connection);
                var group = new VaultGroup { Name = "工作" };
                var tag = new Tag { Name = "开发", Color = "#0A84FF" };
                await repository.InsertGroupAsync(group);
                await repository.InsertTagAsync(tag);

                var entry = new VaultEntry
                {
                    Title = "GitHub",
                    Username = "developer@example.com",
                    Url = "https://github.com",
                    GroupId = group.Id,
                    IsFavorite = true,
                    Notes = "端到端测试",
                    CustomFields = [new CustomField { Key = "环境", Value = "生产" }],
                };
                passwordPlaintext = Encoding.UTF8.GetBytes("correct horse battery staple");
                var encrypted = EntryPasswordCrypto.Encrypt(
                    crypto, vaultKey, entry.Id, passwordPlaintext);
                entry.PasswordEncrypted = [.. encrypted.nonce, .. encrypted.ciphertext, .. encrypted.tag];

                await repository.InsertEntryAsync(entry);
                await repository.SetEntryTagsAsync(entry.Id, [tag.Id]);
                await repository.InsertHistoryAsync(new EntryHistory
                {
                    EntryId = entry.Id,
                    SnapshotJson = "{\"title\":\"GitHub\"}",
                    ChangeDescription = "创建条目",
                });

                fileManager.SaveAndEncrypt(vaultPath, opened.connection, opened.metadata, vaultKey);
            }
            finally
            {
                opened.connection.Dispose();
            }

            var backup = new BackupService();
            backup.Configure(backupDirectory, maxBackups: 5);
            await backup.CreateBackupAsync(vaultPath);
            var backups = await backup.GetBackupsAsync(vaultPath);
            backups.Should().ContainSingle();
            await backup.RestoreFromBackupAsync(backups[0].FilePath, restoredPath);

            var restored = fileManager.OpenAndDecrypt(restoredPath, password);
            try
            {
                byte[] restoredKey = crypto.DeriveKey(
                    password,
                    restored.metadata.Salt,
                    restored.metadata.Argon2MemoryKB,
                    restored.metadata.Argon2Iterations,
                    restored.metadata.Argon2Parallelism);
                try
                {
                    using var repository = new VaultRepository(restored.connection);
                    VaultEntry entry = (await repository.SearchEntriesAsync("GitHub")).Should().ContainSingle().Which;
                    entry.Username.Should().Be("developer@example.com");
                    (await repository.GetTagsForEntryAsync(entry.Id)).Should().ContainSingle(t => t.Name == "开发");
                    (await repository.GetEntryHistoryAsync(entry.Id)).Should().ContainSingle();

                    byte[] blob = entry.PasswordEncrypted!;
                    byte[] decrypted = EntryPasswordCrypto.Decrypt(
                        crypto, restoredKey, entry.Id, blob[12..^16], blob[..12], blob[^16..]);
                    try
                    {
                        decrypted.Should().Equal(passwordPlaintext);
                    }
                    finally
                    {
                        CryptographicOperations.ZeroMemory(decrypted);
                    }
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(restoredKey);
                }
            }
            finally
            {
                restored.connection.Dispose();
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(password);
            if (vaultKey != null)
                CryptographicOperations.ZeroMemory(vaultKey);
            if (passwordPlaintext != null)
                CryptographicOperations.ZeroMemory(passwordPlaintext);
            Array.Clear(recovery.Words);
        }
    }
}
