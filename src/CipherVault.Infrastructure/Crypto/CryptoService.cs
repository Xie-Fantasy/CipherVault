namespace CipherVault.Infrastructure.Crypto;

using System.Security.Cryptography;
using System.Text;
using CipherVault.Core.Exceptions;
using CipherVault.Core.Interfaces;

/// <summary>
/// Implements <see cref="ICryptoService"/> using AES-256-GCM for symmetric encryption
/// and Argon2id for key derivation. This is the cryptographic core of CipherVault.
/// </summary>
public sealed class CryptoService : ICryptoService
{
    private const int NonceSizeBytes = 12;   // 96-bit nonce for AES-GCM
    private const int TagSizeBytes = 16;     // 128-bit authentication tag
    private const int KeySizeBytes = 32;     // 256-bit key

    /// <inheritdoc />
    /// <summary>
    /// Derives a 256-bit key from the given password and salt using Argon2id.
    /// </summary>
    public byte[] DeriveKey(byte[] password, byte[] salt, int memoryKB, int iterations, int parallelism)
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(salt);

        return Argon2KeyDeriver.DeriveKey(password, salt, memoryKB, iterations, parallelism);
    }

    /// <inheritdoc />
    /// <summary>
    /// Derives a recovery key using the BIP39 seed derivation rules with an empty passphrase.
    /// Both mnemonic and salt are NFKD-normalized; PBKDF2-HMAC-SHA512 uses 2048 iterations.
    /// CipherVault uses the first 32 seed bytes as its AES-256 recovery key.
    /// </summary>
    public byte[] DeriveRecoveryKey(string[] mnemonicWords)
    {
        ArgumentNullException.ThrowIfNull(mnemonicWords);

        if (mnemonicWords.Length == 0)
            throw new ArgumentException("Mnemonic words array must not be empty.", nameof(mnemonicWords));

        string mnemonic = string.Join(" ", mnemonicWords).Normalize(NormalizationForm.FormKD);
        string normalizedSalt = "mnemonic".Normalize(NormalizationForm.FormKD);
        byte[] password = Encoding.UTF8.GetBytes(mnemonic);
        byte[] salt = Encoding.UTF8.GetBytes(normalizedSalt);

        try
        {
            using var pbkdf2 = new Rfc2898DeriveBytes(
                password,
                salt,
                iterations: 2048,
                HashAlgorithmName.SHA512);

            return pbkdf2.GetBytes(KeySizeBytes);
        }
        finally
        {
            SecureStringHelper.SecureZeroArray(password);
            SecureStringHelper.SecureZeroArray(salt);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Compatibility only for vaults created by pre-release builds. New vaults never use
    /// this PBKDF2-HMAC-SHA256 variant.
    /// </remarks>
    public byte[] DeriveLegacyRecoveryKey(string[] mnemonicWords)
    {
        ArgumentNullException.ThrowIfNull(mnemonicWords);
        if (mnemonicWords.Length == 0)
            throw new ArgumentException("Mnemonic words array must not be empty.", nameof(mnemonicWords));

        byte[] password = Encoding.UTF8.GetBytes(string.Join(" ", mnemonicWords));
        byte[] salt = Encoding.UTF8.GetBytes("mnemonic");
        try
        {
            using var pbkdf2 = new Rfc2898DeriveBytes(
                password,
                salt,
                iterations: 2048,
                HashAlgorithmName.SHA256);
            return pbkdf2.GetBytes(KeySizeBytes);
        }
        finally
        {
            SecureStringHelper.SecureZeroArray(password);
            SecureStringHelper.SecureZeroArray(salt);
        }
    }

    /// <inheritdoc />
    /// <summary>
    /// Encrypts plaintext using AES-256-GCM with a randomly generated 12-byte nonce.
    /// Returns the ciphertext, nonce, and 16-byte authentication tag.
    /// </summary>
    public (byte[] ciphertext, byte[] nonce, byte[] tag) Encrypt(byte[] key, byte[] plaintext)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(plaintext);

        if (key.Length != KeySizeBytes)
            throw new ArgumentException($"Key must be exactly {KeySizeBytes} bytes (256 bits).", nameof(key));

        // Generate random 12-byte nonce
        byte[] nonce = new byte[NonceSizeBytes];
        RandomNumberGenerator.Fill(nonce);

        // Allocate output arrays
        byte[] ciphertext = new byte[plaintext.Length];
        byte[] tag = new byte[TagSizeBytes];

        // Perform in-place encryption
        using var aes = new AesGcm(key, TagSizeBytes);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        return (ciphertext, nonce, tag);
    }

    /// <inheritdoc />
    /// <summary>
    /// Decrypts ciphertext using AES-256-GCM and verifies the authentication tag.
    /// Throws <see cref="VaultCorruptedException"/> if the tag does not match (data tampered or wrong key).
    /// </summary>
    public byte[] Decrypt(byte[] key, byte[] ciphertext, byte[] nonce, byte[] tag)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(ciphertext);
        ArgumentNullException.ThrowIfNull(nonce);
        ArgumentNullException.ThrowIfNull(tag);

        if (key.Length != KeySizeBytes)
            throw new ArgumentException($"Key must be exactly {KeySizeBytes} bytes (256 bits).", nameof(key));

        if (nonce.Length != NonceSizeBytes)
            throw new ArgumentException($"Nonce must be exactly {NonceSizeBytes} bytes.", nameof(nonce));

        if (tag.Length != TagSizeBytes)
            throw new ArgumentException($"Tag must be exactly {TagSizeBytes} bytes.", nameof(tag));

        byte[] plaintext = new byte[ciphertext.Length];

        try
        {
            using var aes = new AesGcm(key, TagSizeBytes);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
        }
        catch (CryptographicException ex)
        {
            // GCM tag mismatch: data is corrupted or wrong key was used
            SecureStringHelper.SecureZeroArray(plaintext);
            throw new VaultCorruptedException(
                "Decryption failed: authentication tag mismatch. The vault data may be corrupted or the wrong key was used.",
                ex);
        }

        return plaintext;
    }

    /// <inheritdoc />
    /// <summary>
    /// Generates a cryptographically secure random byte array of the specified length.
    /// </summary>
    public byte[] GenerateRandomBytes(int length)
    {
        if (length < 0)
            throw new ArgumentOutOfRangeException(nameof(length), "Length must be non-negative.");

        if (length == 0)
            return Array.Empty<byte>();

        byte[] bytes = new byte[length];
        RandomNumberGenerator.Fill(bytes);
        return bytes;
    }

    /// <inheritdoc />
    /// <summary>
    /// Generates a cryptographically secure random salt.
    /// Default length is 32 bytes (256 bits).
    /// </summary>
    public byte[] GenerateSalt(int length = 32)
    {
        if (length < 16)
            throw new ArgumentOutOfRangeException(nameof(length), "Salt must be at least 16 bytes for security.");

        return GenerateRandomBytes(length);
    }
}
