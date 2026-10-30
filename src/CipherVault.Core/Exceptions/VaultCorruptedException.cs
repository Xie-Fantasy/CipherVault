namespace CipherVault.Core.Exceptions;

public class VaultCorruptedException : Exception
{
    public VaultCorruptedException() : base("Vault file is corrupted.") { }
    public VaultCorruptedException(string message) : base(message) { }
    public VaultCorruptedException(string message, Exception inner) : base(message, inner) { }
}
