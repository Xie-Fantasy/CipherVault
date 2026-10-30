namespace CipherVault.Infrastructure.Security;

/// <summary>
/// Low-level Windows Hello key provider used by <see cref="WindowsHelloService"/>.
/// The abstraction keeps filesystem and blob-format behavior independently testable.
/// </summary>
public interface IWindowsHelloKeyProtector
{
    Task<bool> IsSupportedAsync();

    byte[] Protect(string keyName, ReadOnlySpan<byte> plaintext);

    byte[] Unprotect(string keyName, ReadOnlySpan<byte> ciphertext);

    bool KeyExists(string keyName);

    void DeleteKey(string keyName);
}
