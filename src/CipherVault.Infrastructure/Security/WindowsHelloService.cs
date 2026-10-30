namespace CipherVault.Infrastructure.Security;

using System.Security.Cryptography;
using System.Text;
using CipherVault.Core.Interfaces;
using Windows.Security.Credentials;

/// <summary>
/// Binds the derived 256-bit vault key to a non-exportable RSA key in the
/// Microsoft Passport (Windows Hello) key storage provider.
/// </summary>
/// <remarks>
/// Only RSA-encrypted ciphertext is stored on disk. The private key cannot be
/// exported and each decrypt operation requires Windows Hello user verification.
/// This avoids the bypass in the old design where a consent dialog merely guarded
/// a DPAPI blob that another process running as the same user could decrypt.
/// </remarks>
public sealed class WindowsHelloService : IWindowsHelloService
{
    private static readonly byte[] BlobMagic = "CVHELLO1"u8.ToArray();
    private const int MasterKeyLength = 32;
    private const int MaximumBlobLength = 16 * 1024;

    private readonly string _keysDirectory;
    private readonly IWindowsHelloKeyProtector _protector;

    public WindowsHelloService(
        string? keysDirectory = null,
        IWindowsHelloKeyProtector? protector = null)
    {
        _keysDirectory = keysDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CipherVault",
            "hello_keys");
        _protector = protector ?? new PassportKeyProtector();
        PurgeLegacyStoredKeys();
    }

    /// <inheritdoc />
    public Task<bool> IsAvailableAsync() => _protector.IsSupportedAsync();

    /// <inheritdoc />
    public async Task StoreKeyAsync(string vaultFilePath, byte[] masterKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultFilePath);
        ArgumentNullException.ThrowIfNull(masterKey);
        if (masterKey.Length != MasterKeyLength)
            throw new ArgumentException("Vault key must be exactly 32 bytes.", nameof(masterKey));

        if (!await IsAvailableAsync().ConfigureAwait(false))
            throw new NotSupportedException("此设备未配置可用的 Windows Hello。");

        string keyId = GetVaultId(vaultFilePath);
        string keyName = GetKeyName(keyId);
        string blobPath = GetBlobPath(keyId);
        string temporaryPath = blobPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        byte[]? encryptedKey = null;

        try
        {
            encryptedKey = await Task.Run(() => _protector.Protect(keyName, masterKey))
                .ConfigureAwait(false);
            if (encryptedKey.Length is <= 0 or > MaximumBlobLength)
                throw new CryptographicException("Windows Hello returned an invalid protected key.");

            Directory.CreateDirectory(_keysDirectory);
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                useAsync: true))
            {
                await stream.WriteAsync(BlobMagic).ConfigureAwait(false);
                byte[] lengthBytes = BitConverter.GetBytes(encryptedKey.Length);
                await stream.WriteAsync(lengthBytes).ConfigureAwait(false);
                await stream.WriteAsync(encryptedKey).ConfigureAwait(false);
                await stream.FlushAsync().ConfigureAwait(false);
            }

            File.Move(temporaryPath, blobPath, overwrite: true);
        }
        catch
        {
            BestEffortDelete(temporaryPath, overwrite: false);
            throw;
        }
        finally
        {
            if (encryptedKey is { Length: > 0 })
                CryptographicOperations.ZeroMemory(encryptedKey);
        }
    }

    /// <inheritdoc />
    public async Task<byte[]?> RetrieveKeyAsync(string vaultFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultFilePath);

        string keyId = GetVaultId(vaultFilePath);
        string keyName = GetKeyName(keyId);
        string blobPath = GetBlobPath(keyId);
        if (!File.Exists(blobPath) || !_protector.KeyExists(keyName))
            return null;

        byte[] encryptedKey;
        try
        {
            encryptedKey = await ReadBlobAsync(blobPath).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return null;
        }

        try
        {
            byte[] plaintext = await Task.Run(() => _protector.Unprotect(keyName, encryptedKey))
                .ConfigureAwait(false);
            if (plaintext.Length == MasterKeyLength)
                return plaintext;

            CryptographicOperations.ZeroMemory(plaintext);
            return null;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encryptedKey);
        }
    }

    /// <inheritdoc />
    public void RemoveStoredKey(string vaultFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultFilePath);
        string keyId = GetVaultId(vaultFilePath);
        BestEffortDelete(GetBlobPath(keyId), overwrite: true);
        try
        {
            _protector.DeleteKey(GetKeyName(keyId));
        }
        catch
        {
            // A locked or already-removed provider key is harmless.
        }
    }

    /// <inheritdoc />
    public bool HasStoredKey(string vaultFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultFilePath);
        string keyId = GetVaultId(vaultFilePath);
        return File.Exists(GetBlobPath(keyId)) && _protector.KeyExists(GetKeyName(keyId));
    }

    private async Task<byte[]> ReadBlobAsync(string path)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: true);
        if (stream.Length < BlobMagic.Length + sizeof(int) ||
            stream.Length > BlobMagic.Length + sizeof(int) + MaximumBlobLength)
        {
            throw new InvalidDataException("Invalid Windows Hello key blob length.");
        }

        byte[] magic = new byte[BlobMagic.Length];
        await stream.ReadExactlyAsync(magic).ConfigureAwait(false);
        if (!CryptographicOperations.FixedTimeEquals(magic, BlobMagic))
            throw new InvalidDataException("Invalid Windows Hello key blob header.");

        byte[] lengthBytes = new byte[sizeof(int)];
        await stream.ReadExactlyAsync(lengthBytes).ConfigureAwait(false);
        int length = BitConverter.ToInt32(lengthBytes);
        if (length <= 0 || length > MaximumBlobLength || stream.Length != stream.Position + length)
            throw new InvalidDataException("Invalid Windows Hello key blob payload.");

        byte[] encryptedKey = new byte[length];
        await stream.ReadExactlyAsync(encryptedKey).ConfigureAwait(false);
        return encryptedKey;
    }

    private string GetBlobPath(string keyId) => Path.Combine(_keysDirectory, keyId + ".hello");

    private static string GetKeyName(string keyId) => "CipherVault-" + keyId;

    private static string GetVaultId(string vaultFilePath)
    {
        string canonicalPath = Path.GetFullPath(vaultFilePath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToUpperInvariant();
        byte[] pathBytes = Encoding.UTF8.GetBytes(canonicalPath);
        try
        {
            return Convert.ToHexString(SHA256.HashData(pathBytes));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pathBytes);
        }
    }

    private void PurgeLegacyStoredKeys()
    {
        try
        {
            if (!Directory.Exists(_keysDirectory))
                return;

            string resolvedDirectory = Path.GetFullPath(_keysDirectory)
                .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            foreach (string candidate in Directory.EnumerateFiles(
                resolvedDirectory,
                "*.dat",
                SearchOption.TopDirectoryOnly))
            {
                string resolvedFile = Path.GetFullPath(candidate);
                if (resolvedFile.StartsWith(resolvedDirectory, StringComparison.OrdinalIgnoreCase))
                    BestEffortDelete(resolvedFile, overwrite: true);
            }
        }
        catch
        {
            // Migration cleanup is best-effort and must never prevent startup.
        }
    }

    private static void BestEffortDelete(string path, bool overwrite)
    {
        try
        {
            if (!File.Exists(path))
                return;

            if (overwrite)
            {
                long length = new FileInfo(path).Length;
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
                byte[] buffer = new byte[Math.Min(Math.Max(length, 1), 4096)];
                try
                {
                    long remaining = length;
                    while (remaining > 0)
                    {
                        RandomNumberGenerator.Fill(buffer);
                        int count = (int)Math.Min(buffer.Length, remaining);
                        stream.Write(buffer, 0, count);
                        remaining -= count;
                    }

                    stream.Flush(flushToDisk: true);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(buffer);
                }
            }

            File.Delete(path);
        }
        catch
        {
            // Best effort; a locked file can be retried later.
        }
    }

    private sealed class PassportKeyProtector : IWindowsHelloKeyProtector
    {
        private static readonly CngProvider PassportProvider =
            new("Microsoft Passport Key Storage Provider");

        public async Task<bool> IsSupportedAsync()
        {
            if (!OperatingSystem.IsWindowsVersionAtLeast(10))
                return false;

            try
            {
                return await KeyCredentialManager.IsSupportedAsync();
            }
            catch
            {
                return false;
            }
        }

        public byte[] Protect(string keyName, ReadOnlySpan<byte> plaintext)
        {
            using CngKey key = OpenOrCreateKey(keyName);
            using var rsa = new RSACng(key);
            byte[] copy = plaintext.ToArray();
            try
            {
                return rsa.Encrypt(copy, RSAEncryptionPadding.OaepSHA256);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(copy);
            }
        }

        public byte[] Unprotect(string keyName, ReadOnlySpan<byte> ciphertext)
        {
            using CngKey key = CngKey.Open(keyName, PassportProvider);
            using var rsa = new RSACng(key);
            return rsa.Decrypt(ciphertext.ToArray(), RSAEncryptionPadding.OaepSHA256);
        }

        public bool KeyExists(string keyName)
        {
            try
            {
                return CngKey.Exists(keyName, PassportProvider);
            }
            catch
            {
                return false;
            }
        }

        public void DeleteKey(string keyName)
        {
            if (!KeyExists(keyName))
                return;

            using CngKey key = CngKey.Open(keyName, PassportProvider);
            key.Delete();
        }

        private static CngKey OpenOrCreateKey(string keyName)
        {
            if (CngKey.Exists(keyName, PassportProvider))
                return CngKey.Open(keyName, PassportProvider);

            var parameters = new CngKeyCreationParameters
            {
                Provider = PassportProvider,
                ExportPolicy = CngExportPolicies.None,
                KeyUsage = CngKeyUsages.Decryption,
                UIPolicy = new CngUIPolicy(
                    CngUIProtectionLevels.ForceHighProtection,
                    "CipherVault",
                    "使用 Windows Hello 解锁此保险库",
                    "CipherVault Windows Hello")
            };
            return CngKey.Create(CngAlgorithm.Rsa, keyName, parameters);
        }
    }
}
