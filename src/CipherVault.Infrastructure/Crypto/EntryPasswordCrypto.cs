namespace CipherVault.Infrastructure.Crypto;

using System.Security.Cryptography;
using System.Text;
using CipherVault.Core.Exceptions;
using CipherVault.Core.Interfaces;

/// <summary>
/// Encrypts each password under a key derived for its entry ID. The stored blob remains
/// Nonce(12) + Ciphertext + Tag(16). Authentication allows a safe legacy-key fallback for
/// vaults produced before per-entry derivation was introduced.
/// </summary>
public static class EntryPasswordCrypto
{
    private static readonly byte[] Context = Encoding.UTF8.GetBytes("CipherVault.EntryPassword.v1\0");

    public static (byte[] ciphertext, byte[] nonce, byte[] tag) Encrypt(
        ICryptoService cryptoService,
        byte[] vaultKey,
        string entryId,
        byte[] plaintext)
    {
        ArgumentNullException.ThrowIfNull(cryptoService);
        ArgumentNullException.ThrowIfNull(vaultKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);
        ArgumentNullException.ThrowIfNull(plaintext);

        byte[] entryKey = DeriveKey(vaultKey, entryId);
        try
        {
            return cryptoService.Encrypt(entryKey, plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(entryKey);
        }
    }

    public static byte[] Decrypt(
        ICryptoService cryptoService,
        byte[] vaultKey,
        string entryId,
        byte[] ciphertext,
        byte[] nonce,
        byte[] tag)
    {
        ArgumentNullException.ThrowIfNull(cryptoService);
        ArgumentNullException.ThrowIfNull(vaultKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);

        byte[] entryKey = DeriveKey(vaultKey, entryId);
        try
        {
            try
            {
                return cryptoService.Decrypt(entryKey, ciphertext, nonce, tag);
            }
            catch (VaultCorruptedException)
            {
                // Pre-release vaults encrypted password fields directly with VaultKey.
                // AES-GCM authentication ensures only a genuinely legacy blob succeeds.
                return cryptoService.Decrypt(vaultKey, ciphertext, nonce, tag);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(entryKey);
        }
    }

    private static byte[] DeriveKey(byte[] vaultKey, string entryId)
    {
        byte[] idBytes = Encoding.UTF8.GetBytes(entryId);
        byte[] input = new byte[Context.Length + idBytes.Length];
        Buffer.BlockCopy(Context, 0, input, 0, Context.Length);
        Buffer.BlockCopy(idBytes, 0, input, Context.Length, idBytes.Length);
        try
        {
            return HMACSHA256.HashData(vaultKey, input);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(idBytes);
            CryptographicOperations.ZeroMemory(input);
        }
    }
}
