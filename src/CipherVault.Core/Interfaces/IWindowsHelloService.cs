using System.Threading.Tasks;

namespace CipherVault.Core.Interfaces;

/// <summary>
/// Protects a vault key with a non-exportable Windows Hello credential.
/// </summary>
public interface IWindowsHelloService
{
    /// <summary>
    /// Checks if Windows Hello is available on this device.
    /// </summary>
    Task<bool> IsAvailableAsync();

    /// <summary>
    /// Stores the vault key using a Windows Hello-bound, non-exportable key.
    /// </summary>
    /// <param name="vaultFilePath">The path to the .cvault file (used to derive a unique storage key).</param>
    /// <param name="masterKey">The 256-bit master key to store.</param>
    Task StoreKeyAsync(string vaultFilePath, byte[] masterKey);

    /// <summary>
    /// Retrieves the stored master key for the given vault. Accessing the private
    /// key invokes Windows Hello (face, fingerprint, or PIN).
    /// Returns null if no binding is stored.
    /// </summary>
    /// <param name="vaultFilePath">The path to the .cvault file.</param>
    Task<byte[]?> RetrieveKeyAsync(string vaultFilePath);

    /// <summary>
    /// Removes the stored key for a vault (e.g., when master password is changed).
    /// </summary>
    /// <param name="vaultFilePath">The path to the .cvault file.</param>
    void RemoveStoredKey(string vaultFilePath);

    /// <summary>
    /// Whether a Hello-bound key exists for the given vault.
    /// </summary>
    /// <param name="vaultFilePath">The path to the .cvault file.</param>
    bool HasStoredKey(string vaultFilePath);
}
