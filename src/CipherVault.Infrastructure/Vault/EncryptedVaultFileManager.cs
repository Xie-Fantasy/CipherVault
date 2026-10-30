namespace CipherVault.Infrastructure.Vault;

using System.Security.Cryptography;
using CipherVault.Core.Exceptions;
using CipherVault.Core.Interfaces;
using CipherVault.Core.Models;
using CipherVault.Core.Security;
using CipherVault.Infrastructure.Crypto;
using Microsoft.Data.Sqlite;

/// <summary>
/// Manages reading and writing of encrypted .cvault files.
/// Handles the binary file format including header, recovery key block, and encrypted payload.
/// </summary>
public sealed class EncryptedVaultFileManager
{
    // Kept only so upgrades can securely remove plaintext session files left by
    // versions prior to 1.0.11. New code never creates files in this directory.
    public static string SessionDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CipherVault",
        "sessions");

    private readonly ICryptoService _crypto;

    // Magic bytes: "CVAULT" + version 1 (0x01) + reserved (0x00)
    private static readonly byte[] MagicBytes = "CVAULT\x01\x00"u8.ToArray();
    private const uint FileVersion = 1;
    private const int SaltSize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int VaultKeySize = 32;
    private const int ChecksumSize = 32; // SHA-256
    private const int RecoveryKeyBlockSize = NonceSize + VaultKeySize + TagSize;
    private const int MaximumRecoveryKeyBlockSize = 4096;
    private const long MaximumEncryptedPayloadSize = 512L * 1024 * 1024;

    public EncryptedVaultFileManager(ICryptoService crypto)
    {
        _crypto = crypto ?? throw new ArgumentNullException(nameof(crypto));
    }

    /// <summary>
    /// Creates a new .cvault file with an empty SQLite database.
    /// </summary>
    public VaultFile CreateNew(
        string filePath,
        string name,
        byte[] masterPassword,
        string[] recoveryMnemonicWords)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(masterPassword);
        ArgumentNullException.ThrowIfNull(recoveryMnemonicWords);

        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("Vault file path must not be empty.", nameof(filePath));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Vault name must not be empty.", nameof(name));
        if (recoveryMnemonicWords.Length == 0)
            throw new ArgumentException("Recovery mnemonic must not be empty.", nameof(recoveryMnemonicWords));
        if (File.Exists(filePath))
            throw new IOException($"A vault file already exists at '{filePath}'.");

        // Generate salt
        byte[] salt = _crypto.GenerateSalt(SaltSize);

        // Default KDF parameters
        int argon2MemoryKB = 65536;
        int argon2Iterations = 3;
        int argon2Parallelism = 4;

        // Derive master key
        byte[] masterKey = _crypto.DeriveKey(masterPassword, salt, argon2MemoryKB, argon2Iterations, argon2Parallelism);
        byte[] recoveryKey = _crypto.DeriveRecoveryKey(recoveryMnemonicWords);
        byte[]? dbBytes = null;
        byte[]? ciphertext = null;
        byte[]? nonce = null;
        byte[]? tag = null;
        byte[]? recoveryCiphertext = null;
        byte[]? recoveryNonce = null;
        byte[]? recoveryTag = null;

        try
        {
            using (var connection = InMemorySqliteDatabase.CreateEmpty())
            {
                Database.SchemaMigrator.InitializeSchema(connection);

                using var metaCmd = connection.CreateCommand();
                metaCmd.CommandText = """
                    INSERT INTO vault_meta (key, value) VALUES ('version', '1');
                    INSERT INTO vault_meta (key, value) VALUES ('vault_name', @name);
                    INSERT INTO vault_meta (key, value) VALUES ('created_at', @created);
                    INSERT INTO vault_meta (key, value) VALUES ('last_modified_at', @modified);
                """;
                metaCmd.Parameters.AddWithValue("@name", name);
                var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
                metaCmd.Parameters.AddWithValue("@created", now);
                metaCmd.Parameters.AddWithValue("@modified", now);
                metaCmd.ExecuteNonQuery();

                dbBytes = InMemorySqliteDatabase.Serialize(connection);
            }

            // Encrypt the database
            (ciphertext, nonce, tag) = _crypto.Encrypt(masterKey, dbBytes);

            // The payload key is wrapped by the recovery-derived key. In format v1 the
            // payload key is the Argon2-derived master key, so recovery returns exactly
            // the key required both for the payload and per-entry password fields.
            (recoveryCiphertext, recoveryNonce, recoveryTag) = _crypto.Encrypt(recoveryKey, masterKey);
            byte[] recoveryKeyBlock = CombineAeadBlock(recoveryNonce, recoveryCiphertext, recoveryTag);

            // Build the VaultFile metadata
            var vaultFile = new VaultFile
            {
                Name = name,
                FilePath = filePath,
                Salt = salt,
                Argon2MemoryKB = argon2MemoryKB,
                Argon2Iterations = argon2Iterations,
                Argon2Parallelism = argon2Parallelism,
                FailedAttempts = 0,
                LockoutUntil = null,
                RecoveryKeyBlock = recoveryKeyBlock,
            };

            // Write the file
            WriteVaultFile(filePath, vaultFile, ciphertext, nonce, tag, replaceExisting: false);

            return vaultFile;
        }
        finally
        {
            ZeroSensitive(masterKey);
            ZeroSensitive(recoveryKey);
            ZeroSensitive(dbBytes);
            ZeroSensitive(ciphertext);
            ZeroSensitive(nonce);
            ZeroSensitive(tag);
            ZeroSensitive(recoveryCiphertext);
            ZeroSensitive(recoveryNonce);
            ZeroSensitive(recoveryTag);
        }
    }

    /// <summary>
    /// Opens a .cvault file, decrypts the payload using the master password,
    /// and returns an open SQLite connection to the decrypted database.
    /// </summary>
    public (SqliteConnection connection, VaultFile metadata) OpenAndDecrypt(string filePath, byte[] masterPassword)
    {
        var opened = OpenAndDecryptWithKey(filePath, masterPassword);
        try
        {
            return (opened.connection, opened.metadata);
        }
        finally
        {
            ZeroSensitive(opened.vaultKey);
        }
    }

    /// <summary>
    /// Opens a vault and transfers the already-derived payload key to the caller.
    /// This avoids running the memory-hard Argon2id derivation a second time when
    /// the application installs the key into locked memory after a successful unlock.
    /// The caller owns <c>vaultKey</c> and must clear or securely store it.
    /// </summary>
    public (SqliteConnection connection, VaultFile metadata, byte[] vaultKey) OpenAndDecryptWithKey(
        string filePath,
        byte[] masterPassword)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(masterPassword);

        if (!File.Exists(filePath))
            throw new FileNotFoundException("Vault file not found.", filePath);

        var (vaultFile, ciphertext, nonce, tag) = ReadVaultFile(filePath);
        byte[]? masterKey = null;
        byte[]? dbBytes = null;
        SqliteConnection? connection = null;
        bool ownershipTransferred = false;

        try
        {
            masterKey = _crypto.DeriveKey(
                masterPassword,
                vaultFile.Salt,
                vaultFile.Argon2MemoryKB,
                vaultFile.Argon2Iterations,
                vaultFile.Argon2Parallelism);

            try
            {
                dbBytes = _crypto.Decrypt(masterKey, ciphertext, nonce, tag);
            }
            catch (VaultCorruptedException)
            {
                throw new InvalidMasterPasswordException(
                    (int)(vaultFile.FailedAttempts < 20 ? 20 - vaultFile.FailedAttempts : 0));
            }

            connection = InMemorySqliteDatabase.Open(dbBytes);
            bool migrated = InitializeOpenedConnection(connection);
            if (migrated)
                SaveAndEncrypt(filePath, connection, vaultFile, masterKey);

            vaultFile.FilePath = filePath;
            ownershipTransferred = true;
            return (connection, vaultFile, masterKey);
        }
        finally
        {
            ZeroSensitive(dbBytes);
            ZeroSensitive(ciphertext);
            ZeroSensitive(nonce);
            ZeroSensitive(tag);

            if (!ownershipTransferred)
            {
                connection?.Dispose();
                ZeroSensitive(masterKey);
            }
        }
    }

    /// <summary>
    /// Encrypts the in-memory SQLite database and writes it back to the .cvault file.
    /// </summary>
    public void SaveAndEncrypt(string filePath, SqliteConnection connection, VaultFile metadata, byte[] masterKey)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(masterKey);

        byte[]? dbBytes = null;
        byte[]? ciphertext = null;
        byte[]? nonce = null;
        byte[]? tag = null;
        try
        {
            dbBytes = InMemorySqliteDatabase.Serialize(connection);
            if (dbBytes.LongLength > MaximumEncryptedPayloadSize)
                throw new IOException("Decrypted database exceeds the supported vault size limit.");

            // Encrypt
            (ciphertext, nonce, tag) = _crypto.Encrypt(masterKey, dbBytes);

            // Update metadata
            metadata.LastModifiedAt = DateTime.UtcNow;

            // Write to a sibling temporary file, flush it, and atomically replace the
            // existing vault. A failed write therefore leaves the previous vault intact.
            WriteVaultFile(filePath, metadata, ciphertext, nonce, tag, replaceExisting: true);
        }
        finally
        {
            ZeroSensitive(dbBytes);
        }
    }

    /// <summary>
    /// Opens a .cvault file using a recovery key (mnemonic words) instead of the master password.
    /// Decrypts the recovery key block to obtain the vault key, then decrypts the payload.
    /// </summary>
    public (SqliteConnection connection, VaultFile metadata, byte[] vaultKey) OpenWithRecoveryKey(
        string filePath,
        string[] mnemonicWords)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(mnemonicWords);

        if (!File.Exists(filePath))
            throw new FileNotFoundException("Vault file not found.", filePath);

        // New vaults use 12-word BIP39 mnemonics. Keep accepting the former
        // 16-word format so existing pre-release vaults remain recoverable;
        // its authenticity is still verified by the AES-GCM recovery block.
        if (mnemonicWords.Length == RecoveryKeyGenerator.MnemonicWordCount)
        {
            if (!new RecoveryKeyGenerator().Validate(new Core.Models.RecoveryKey { Words = mnemonicWords }))
                throw new InvalidRecoveryKeyException("The recovery phrase has an invalid BIP39 checksum.");
        }
        else if (mnemonicWords.Length != 16)
        {
            throw new InvalidRecoveryKeyException(
                $"The recovery phrase must contain {RecoveryKeyGenerator.MnemonicWordCount} words.");
        }

        // Read the file
        var (vaultFile, ciphertext, nonce, tag) = ReadVaultFile(filePath);

        byte[]? recoveryKey = null;
        byte[]? rkNonce = null;
        byte[]? rkCiphertext = null;
        byte[]? rkTag = null;
        byte[]? vaultKey = null;
        byte[]? dbBytes = null;
        bool vaultKeyTransferred = false;
        try
        {
            // New vaults follow BIP39 PBKDF2-HMAC-SHA512. If that does not authenticate,
            // retry the pre-release SHA256 derivation so existing vaults can be migrated.
            recoveryKey = _crypto.DeriveRecoveryKey(mnemonicWords);

            // Format v1 recovery block is exactly:
            // Nonce(12B) + encrypted vault key(32B) + Tag(16B).
            if (vaultFile.RecoveryKeyBlock.Length != RecoveryKeyBlockSize)
                throw new InvalidRecoveryKeyException("Recovery key block is missing or invalid.");

            rkNonce = vaultFile.RecoveryKeyBlock[..NonceSize];
            rkCiphertext = vaultFile.RecoveryKeyBlock[NonceSize..^TagSize];
            rkTag = vaultFile.RecoveryKeyBlock[^TagSize..];

            try
            {
                vaultKey = _crypto.Decrypt(recoveryKey, rkCiphertext, rkNonce, rkTag);
            }
            catch (VaultCorruptedException)
            {
                ZeroSensitive(recoveryKey);
                recoveryKey = _crypto.DeriveLegacyRecoveryKey(mnemonicWords);
                try
                {
                    vaultKey = _crypto.Decrypt(recoveryKey, rkCiphertext, rkNonce, rkTag);
                }
                catch (VaultCorruptedException)
                {
                    throw new InvalidRecoveryKeyException("The recovery key does not match this vault.");
                }
            }

            if (vaultKey.Length != VaultKeySize)
                throw new VaultCorruptedException("Recovery block did not contain a valid vault key.");

            // Decrypt the payload with the recovered key.
            try
            {
                dbBytes = _crypto.Decrypt(vaultKey, ciphertext, nonce, tag);
            }
            catch (VaultCorruptedException ex)
            {
                throw new VaultCorruptedException("Failed to decrypt vault payload with recovered key.", ex);
            }

            var connection = InMemorySqliteDatabase.Open(dbBytes);
            bool migrated = InitializeOpenedConnection(connection);
            if (migrated)
                SaveAndEncrypt(filePath, connection, vaultFile, vaultKey);

            vaultFile.FilePath = filePath;
            vaultKeyTransferred = true;
            return (connection, vaultFile, vaultKey);
        }
        finally
        {
            ZeroSensitive(recoveryKey);
            ZeroSensitive(rkNonce);
            ZeroSensitive(rkCiphertext);
            ZeroSensitive(rkTag);
            ZeroSensitive(dbBytes);
            ZeroSensitive(ciphertext);
            ZeroSensitive(nonce);
            ZeroSensitive(tag);

            if (!vaultKeyTransferred)
                ZeroSensitive(vaultKey);
        }
    }

    /// <summary>
    /// Opens a .cvault file using a pre-derived master key (for example, unwrapped by Windows Hello).
    /// Unlike <see cref="OpenAndDecrypt"/>, this method does not derive the key from a password.
    /// </summary>
    public (SqliteConnection connection, VaultFile metadata) OpenWithMasterKey(string filePath, byte[] masterKey)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(masterKey);

        if (!File.Exists(filePath))
            throw new FileNotFoundException("Vault file not found.", filePath);

        // Read the file header and encrypted payload
        var (vaultFile, ciphertext, nonce, tag) = ReadVaultFile(filePath);

        // Decrypt the payload directly with the provided master key
        byte[]? dbBytes = null;
        try
        {
            dbBytes = _crypto.Decrypt(masterKey, ciphertext, nonce, tag);
        }
        catch (VaultCorruptedException)
        {
            throw new InvalidMasterPasswordException(
                (int)(vaultFile.FailedAttempts < 20 ? 20 - vaultFile.FailedAttempts : 0));
        }
        finally
        {
            ZeroSensitive(ciphertext);
            ZeroSensitive(nonce);
            ZeroSensitive(tag);
        }

        try
        {
            var connection = InMemorySqliteDatabase.Open(dbBytes);
            bool migrated = InitializeOpenedConnection(connection);
            if (migrated)
                SaveAndEncrypt(filePath, connection, vaultFile, masterKey);

            vaultFile.FilePath = filePath;
            return (connection, vaultFile);
        }
        finally
        {
            ZeroSensitive(dbBytes);
        }
    }

    /// <summary>
    /// Updates the failed attempts count and lockout timestamp in the file header
    /// without decrypting or modifying the encrypted payload.
    /// </summary>
    public void UpdateFailedAttempts(string filePath, uint failedAttempts, DateTime? lockoutUntil)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        if (!File.Exists(filePath))
            throw new FileNotFoundException("Vault file not found.", filePath);

        // Read the full file to modify header fields
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite);
        using var reader = new BinaryReader(stream);
        using var writer = new BinaryWriter(stream);

        // Skip magic (8) + version (4) + salt (32) + argon2 params (12) = 56 bytes
        stream.Position = 8 + 4 + 32 + 4 + 4 + 4;

        // Write failed attempts
        writer.Write(failedAttempts);

        // Write lockout until as Unix timestamp
        uint lockoutUnix = lockoutUntil.HasValue
            ? (uint)new DateTimeOffset(lockoutUntil.Value).ToUnixTimeSeconds()
            : 0;
        writer.Write(lockoutUnix);

        // Flush writer before repositioning stream for checksum computation
        writer.Flush();

        // Recalculate header checksum
        stream.Position = 0;
        // Read up to the checksum position (header is 8+4+32+4+4+4+4+4 = 64 bytes)
        byte[] headerBytes = reader.ReadBytes(64);
        byte[] checksum = SHA256.HashData(headerBytes);

        // Write checksum length and checksum
        writer.Write((uint)ChecksumSize);
        writer.Write(checksum);
        writer.Flush();
    }

    /// <summary>
    /// Reads only the file header without decrypting the payload.
    /// Returns metadata including KDF parameters, failed attempts, and lockout status.
    /// </summary>
    public VaultFile ReadHeader(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        if (!File.Exists(filePath))
            throw new FileNotFoundException("Vault file not found.", filePath);

        var (vaultFile, _, _, _) = ReadVaultFile(filePath);
        return vaultFile;
    }

    /// <summary>
    /// Rotates the format-v1 payload/per-entry key after recovery and atomically
    /// rewrites the vault using a new master password.
    /// </summary>
    public byte[] ResetMasterPassword(
        string filePath,
        SqliteConnection connection,
        VaultFile metadata,
        byte[] oldVaultKey,
        byte[] newMasterPassword,
        string[] recoveryMnemonicWords)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(oldVaultKey);
        ArgumentNullException.ThrowIfNull(newMasterPassword);
        ArgumentNullException.ThrowIfNull(recoveryMnemonicWords);
        if (!MasterPasswordPolicy.IsValidUtf8(newMasterPassword))
        {
            throw new ArgumentException(
                $"The new master password must contain {MasterPasswordPolicy.MinimumLength} to " +
                $"{MasterPasswordPolicy.MaximumLength} characters.",
                nameof(newMasterPassword));
        }

        byte[] newSalt = _crypto.GenerateSalt(SaltSize);
        byte[]? newVaultKey = null;
        byte[]? recoveryKey = null;
        byte[]? recoveryCiphertext = null;
        byte[]? recoveryNonce = null;
        byte[]? recoveryTag = null;
        byte[] oldSalt = metadata.Salt;
        byte[] oldRecoveryBlock = metadata.RecoveryKeyBlock;
        bool passwordsRotated = false;
        bool keyTransferred = false;

        try
        {
            newVaultKey = _crypto.DeriveKey(
                newMasterPassword,
                newSalt,
                metadata.Argon2MemoryKB,
                metadata.Argon2Iterations,
                metadata.Argon2Parallelism);
            recoveryKey = _crypto.DeriveRecoveryKey(recoveryMnemonicWords);
            (recoveryCiphertext, recoveryNonce, recoveryTag) = _crypto.Encrypt(recoveryKey, newVaultKey);

            ReencryptEntryPasswords(connection, oldVaultKey, newVaultKey);
            passwordsRotated = true;

            metadata.Salt = newSalt;
            metadata.RecoveryKeyBlock = CombineAeadBlock(recoveryNonce, recoveryCiphertext, recoveryTag);
            metadata.FailedAttempts = 0;
            metadata.LockoutUntil = null;

            try
            {
                SaveAndEncrypt(filePath, connection, metadata, newVaultKey);
            }
            catch
            {
                metadata.Salt = oldSalt;
                metadata.RecoveryKeyBlock = oldRecoveryBlock;
                if (passwordsRotated)
                    ReencryptEntryPasswords(connection, newVaultKey, oldVaultKey);
                throw;
            }

            keyTransferred = true;
            return newVaultKey;
        }
        finally
        {
            if (!keyTransferred)
                ZeroSensitive(newVaultKey);
            ZeroSensitive(recoveryKey);
            ZeroSensitive(recoveryCiphertext);
            ZeroSensitive(recoveryNonce);
            ZeroSensitive(recoveryTag);
            if (!ReferenceEquals(metadata.Salt, newSalt))
                ZeroSensitive(newSalt);
        }
    }

    private void ReencryptEntryPasswords(SqliteConnection connection, byte[] sourceKey, byte[] targetKey)
    {
        var rows = new List<(string Id, byte[] Value)>();
        using (var select = connection.CreateCommand())
        {
            select.CommandText = "SELECT id, password_enc FROM entries WHERE password_enc IS NOT NULL";
            using var reader = select.ExecuteReader();
            while (reader.Read())
                rows.Add((reader.GetString(0), (byte[])reader[1]));
        }

        using var transaction = connection.BeginTransaction();
        try
        {
            foreach (var row in rows)
            {
                if (row.Value.Length < NonceSize + TagSize)
                    throw new VaultCorruptedException($"Entry '{row.Id}' contains an invalid encrypted password.");

                byte[] nonce = row.Value[..NonceSize];
                byte[] ciphertext = row.Value[NonceSize..^TagSize];
                byte[] tag = row.Value[^TagSize..];
                byte[]? plaintext = null;
                byte[]? rotatedCiphertext = null;
                byte[]? rotatedNonce = null;
                byte[]? rotatedTag = null;
                byte[]? combined = null;
                try
                {
                    plaintext = EntryPasswordCrypto.Decrypt(
                        _crypto, sourceKey, row.Id, ciphertext, nonce, tag);
                    (rotatedCiphertext, rotatedNonce, rotatedTag) = EntryPasswordCrypto.Encrypt(
                        _crypto, targetKey, row.Id, plaintext);
                    combined = CombineAeadBlock(rotatedNonce, rotatedCiphertext, rotatedTag);

                    using var update = connection.CreateCommand();
                    update.Transaction = transaction;
                    update.CommandText = "UPDATE entries SET password_enc = @password WHERE id = @id";
                    update.Parameters.AddWithValue("@password", combined);
                    update.Parameters.AddWithValue("@id", row.Id);
                    update.ExecuteNonQuery();
                }
                finally
                {
                    ZeroSensitive(row.Value);
                    ZeroSensitive(nonce);
                    ZeroSensitive(ciphertext);
                    ZeroSensitive(tag);
                    ZeroSensitive(plaintext);
                    ZeroSensitive(rotatedCiphertext);
                    ZeroSensitive(rotatedNonce);
                    ZeroSensitive(rotatedTag);
                    ZeroSensitive(combined);
                }
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            foreach (var row in rows)
                ZeroSensitive(row.Value);
            throw;
        }
    }

    #region Binary I/O

    private void WriteVaultFile(
        string filePath,
        VaultFile metadata,
        byte[] ciphertext,
        byte[] nonce,
        byte[] tag,
        bool replaceExisting)
    {
        if (metadata.Salt.Length != SaltSize)
            throw new ArgumentException($"Vault salt must be exactly {SaltSize} bytes.", nameof(metadata));
        if (nonce.Length != NonceSize)
            throw new ArgumentException($"Nonce must be exactly {NonceSize} bytes.", nameof(nonce));
        if (tag.Length != TagSize)
            throw new ArgumentException($"Tag must be exactly {TagSize} bytes.", nameof(tag));
        if (metadata.RecoveryKeyBlock.Length is not 0 and not RecoveryKeyBlockSize)
            throw new ArgumentException("Recovery key block has an invalid format.", nameof(metadata));

        ValidateKdfParameters(
            (uint)metadata.Argon2MemoryKB,
            (uint)metadata.Argon2Iterations,
            (uint)metadata.Argon2Parallelism);

        long payloadLength = (long)NonceSize + ciphertext.Length + TagSize;
        if (payloadLength > MaximumEncryptedPayloadSize)
            throw new ArgumentException("Encrypted vault payload exceeds the supported size limit.", nameof(ciphertext));

        // Build the encrypted payload block: Nonce(12B) + Ciphertext + Tag(16B)
        byte[] payloadBlock = new byte[NonceSize + ciphertext.Length + TagSize];
        Buffer.BlockCopy(nonce, 0, payloadBlock, 0, NonceSize);
        Buffer.BlockCopy(ciphertext, 0, payloadBlock, NonceSize, ciphertext.Length);
        Buffer.BlockCopy(tag, 0, payloadBlock, NonceSize + ciphertext.Length, TagSize);

        // Build header bytes in memory to compute checksum before writing to file
        byte[]? headerBytes = null;
        byte[]? checksum = null;
        string fullPath = Path.GetFullPath(filePath);
        string directory = Path.GetDirectoryName(fullPath)
            ?? throw new ArgumentException("Vault path must include a directory.", nameof(filePath));
        Directory.CreateDirectory(directory);
        string tempPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (var headerStream = new MemoryStream())
            using (var headerWriter = new BinaryWriter(headerStream))
            {
                headerWriter.Write(MagicBytes);                                    // 8 bytes
                headerWriter.Write(FileVersion);                                   // 4 bytes
                headerWriter.Write(metadata.Salt);                                 // 32 bytes
                headerWriter.Write((uint)metadata.Argon2MemoryKB);                 // 4 bytes
                headerWriter.Write((uint)metadata.Argon2Iterations);               // 4 bytes
                headerWriter.Write((uint)metadata.Argon2Parallelism);              // 4 bytes
                headerWriter.Write(metadata.FailedAttempts);                       // 4 bytes

                long lockoutSeconds = metadata.LockoutUntil.HasValue
                    ? new DateTimeOffset(metadata.LockoutUntil.Value).ToUnixTimeSeconds()
                    : 0;
                if (lockoutSeconds is < 0 or > uint.MaxValue)
                    throw new ArgumentOutOfRangeException(nameof(metadata), "Lockout timestamp is outside the v1 file-format range.");

                headerWriter.Write((uint)lockoutSeconds);                          // 4 bytes
                headerWriter.Flush();

                headerBytes = headerStream.ToArray(); // exactly 64 bytes
            }

            checksum = SHA256.HashData(headerBytes);

            // Write and durably flush a sibling temporary file first.
            using (var stream = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 64 * 1024,
                options: FileOptions.WriteThrough))
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(headerBytes);
                writer.Write((uint)ChecksumSize);
                writer.Write(checksum);
                writer.Write((uint)metadata.RecoveryKeyBlock.Length);

                if (metadata.RecoveryKeyBlock.Length > 0)
                    writer.Write(metadata.RecoveryKeyBlock);

                writer.Write((ulong)payloadBlock.Length);
                writer.Write(payloadBlock);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            if (replaceExisting && File.Exists(fullPath))
            {
                File.Replace(tempPath, fullPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            else
            {
                // overwrite:false also protects CreateNew against a same-path race.
                File.Move(tempPath, fullPath, overwrite: false);
            }
        }
        finally
        {
            ZeroSensitive(payloadBlock);
            ZeroSensitive(headerBytes);
            ZeroSensitive(checksum);

            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    private (VaultFile metadata, byte[] ciphertext, byte[] nonce, byte[] tag) ReadVaultFile(string filePath)
    {
        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new BinaryReader(stream);

            // === Header ===
            byte[] magic = ReadExact(reader, 8, "magic");
            if (!magic.AsSpan().SequenceEqual(MagicBytes))
                throw new VaultCorruptedException("Invalid file format: magic bytes do not match.");

            uint version = reader.ReadUInt32();
            if (version != FileVersion)
                throw new VaultCorruptedException($"Unsupported file version: {version}. Expected {FileVersion}.");

            byte[] salt = ReadExact(reader, SaltSize, "salt");
            uint argon2MemoryKB = reader.ReadUInt32();
            uint argon2Iterations = reader.ReadUInt32();
            uint argon2Parallelism = reader.ReadUInt32();
            uint failedAttempts = reader.ReadUInt32();
            uint lockoutUnix = reader.ReadUInt32();

            uint checksumLen = reader.ReadUInt32();
            if (checksumLen != ChecksumSize)
                throw new VaultCorruptedException($"Invalid header checksum length: {checksumLen}.");

            byte[] storedChecksum = ReadExact(reader, ChecksumSize, "header checksum");
            long afterChecksumPosition = stream.Position;

            stream.Position = 0;
            byte[] headerBytes = ReadExact(reader, 64, "header");
            byte[] computedChecksum = SHA256.HashData(headerBytes);
            if (!CryptographicOperations.FixedTimeEquals(storedChecksum, computedChecksum))
                throw new VaultCorruptedException("Header checksum mismatch. File may be corrupted.");

            // The checksum pass rewinds the stream; resume after the stored checksum.
            stream.Position = afterChecksumPosition;
            ValidateKdfParameters(argon2MemoryKB, argon2Iterations, argon2Parallelism);
            if (failedAttempts > int.MaxValue)
                throw new VaultCorruptedException("Failed-attempt counter is outside the supported range.");

            DateTime? lockoutUntil = lockoutUnix > 0
                ? DateTimeOffset.FromUnixTimeSeconds(lockoutUnix).UtcDateTime
                : null;

            var vaultFile = new VaultFile
            {
                Salt = salt,
                Argon2MemoryKB = checked((int)argon2MemoryKB),
                Argon2Iterations = checked((int)argon2Iterations),
                Argon2Parallelism = checked((int)argon2Parallelism),
                FailedAttempts = failedAttempts,
                LockoutUntil = lockoutUntil,
            };

            // === Recovery Key Block ===
            uint rkBlockLen = reader.ReadUInt32();
            if (rkBlockLen > MaximumRecoveryKeyBlockSize)
                throw new VaultCorruptedException("Recovery key block exceeds the supported size limit.");
            if (rkBlockLen is not 0 and not RecoveryKeyBlockSize)
                throw new VaultCorruptedException("Recovery key block has an invalid v1 length.");

            EnsureRemaining(stream, rkBlockLen, "recovery key block");
            if (rkBlockLen > 0)
                vaultFile.RecoveryKeyBlock = ReadExact(reader, checked((int)rkBlockLen), "recovery key block");

            // === Encrypted Payload ===
            EnsureRemaining(stream, sizeof(ulong), "payload length");
            ulong payloadLen = reader.ReadUInt64();
            if (payloadLen < (ulong)(NonceSize + TagSize))
                throw new VaultCorruptedException("Encrypted payload is too small.");
            if (payloadLen > (ulong)MaximumEncryptedPayloadSize || payloadLen > int.MaxValue)
                throw new VaultCorruptedException("Encrypted payload exceeds the supported size limit.");

            EnsureRemaining(stream, payloadLen, "encrypted payload");
            byte[] payloadBlock = ReadExact(reader, checked((int)payloadLen), "encrypted payload");
            if (stream.Position != stream.Length)
                throw new VaultCorruptedException("Vault file contains unexpected trailing data.");

            try
            {
                byte[] nonce = payloadBlock[..NonceSize];
                byte[] ciphertext = payloadBlock[NonceSize..^TagSize];
                byte[] tag = payloadBlock[^TagSize..];
                return (vaultFile, ciphertext, nonce, tag);
            }
            finally
            {
                ZeroSensitive(payloadBlock);
            }
        }
        catch (EndOfStreamException ex)
        {
            throw new VaultCorruptedException("Vault file is truncated.", ex);
        }
        catch (OverflowException ex)
        {
            throw new VaultCorruptedException("Vault file contains an invalid length value.", ex);
        }
    }

    private static byte[] CombineAeadBlock(byte[] nonce, byte[] ciphertext, byte[] tag)
    {
        if (nonce.Length != NonceSize || tag.Length != TagSize)
            throw new CryptographicException("Invalid AES-GCM output dimensions.");

        byte[] result = new byte[nonce.Length + ciphertext.Length + tag.Length];
        Buffer.BlockCopy(nonce, 0, result, 0, nonce.Length);
        Buffer.BlockCopy(ciphertext, 0, result, nonce.Length, ciphertext.Length);
        Buffer.BlockCopy(tag, 0, result, nonce.Length + ciphertext.Length, tag.Length);
        return result;
    }

    private static bool InitializeOpenedConnection(SqliteConnection connection)
    {
        using (var pragmaCmd = connection.CreateCommand())
        {
            pragmaCmd.CommandText = """
                PRAGMA journal_mode=MEMORY;
                PRAGMA temp_store=MEMORY;
                PRAGMA foreign_keys=ON;
                """;
            pragmaCmd.ExecuteNonQuery();
        }

        // Every unlock route must migrate an older vault before repositories start
        // issuing queries that reference newly added columns.
        return Database.SchemaMigrator.InitializeSchema(connection);
    }

    private static void ValidateKdfParameters(uint memoryKiB, uint iterations, uint parallelism)
    {
        if (memoryKiB is < Argon2KeyDeriver.MinimumMemoryKiB or > Argon2KeyDeriver.MaximumMemoryKiB)
            throw new VaultCorruptedException("Argon2 memory parameter is outside the supported range.");
        if (iterations is < Argon2KeyDeriver.MinimumIterations or > Argon2KeyDeriver.MaximumIterations)
            throw new VaultCorruptedException("Argon2 iteration parameter is outside the supported range.");
        if (parallelism is < Argon2KeyDeriver.MinimumParallelism or > Argon2KeyDeriver.MaximumParallelism)
            throw new VaultCorruptedException("Argon2 parallelism parameter is outside the supported range.");
    }

    private static byte[] ReadExact(BinaryReader reader, int count, string fieldName)
    {
        byte[] bytes = reader.ReadBytes(count);
        if (bytes.Length != count)
            throw new VaultCorruptedException($"Vault file is truncated while reading {fieldName}.");
        return bytes;
    }

    private static void EnsureRemaining(Stream stream, ulong requiredBytes, string fieldName)
    {
        long remaining = stream.Length - stream.Position;
        if (remaining < 0 || requiredBytes > (ulong)remaining)
            throw new VaultCorruptedException($"Vault file is truncated while reading {fieldName}.");
    }

    private static void ZeroSensitive(byte[]? bytes)
    {
        if (bytes is { Length: > 0 })
            CryptographicOperations.ZeroMemory(bytes);
    }

    #endregion

    #region Legacy Session File Cleanup

    public static bool IsManagedSessionDatabasePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        try
        {
            string directory = Path.GetFullPath(SessionDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            string fullPath = Path.GetFullPath(path);
            string fileName = Path.GetFileName(fullPath);
            return fullPath.StartsWith(directory, StringComparison.OrdinalIgnoreCase)
                && fileName.StartsWith("cvault_", StringComparison.OrdinalIgnoreCase)
                && (fileName.EndsWith(".db", StringComparison.OrdinalIgnoreCase)
                    || fileName.EndsWith(".db-wal", StringComparison.OrdinalIgnoreCase)
                    || fileName.EndsWith(".db-shm", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Best-effort purge of plaintext session files left by an abnormal exit.</summary>
    public static void CleanupOrphanedSessionFiles()
    {
        if (!Directory.Exists(SessionDirectory))
            return;

        foreach (string path in Directory.EnumerateFiles(SessionDirectory, "cvault_*", SearchOption.TopDirectoryOnly))
        {
            if (!IsManagedSessionDatabasePath(path))
                continue;

            try { OverwriteAndDelete(path); }
            catch { /* A later startup gets another chance; never broaden the target. */ }
        }

        try
        {
            if (!Directory.EnumerateFileSystemEntries(SessionDirectory).Any())
                Directory.Delete(SessionDirectory);
        }
        catch
        {
            // The directory may be in use or contain unrelated data; leave it alone.
        }
    }

    private static void OverwriteAndDelete(string path)
    {
        if (!File.Exists(path) || !IsManagedSessionDatabasePath(path))
            return;

        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None))
        {
            byte[] zeros = new byte[64 * 1024];
            long remaining = stream.Length;
            stream.Position = 0;
            while (remaining > 0)
            {
                int count = (int)Math.Min(zeros.Length, remaining);
                stream.Write(zeros, 0, count);
                remaining -= count;
            }
            stream.Flush(flushToDisk: true);
        }

        File.Delete(path);
    }

    #endregion
}
