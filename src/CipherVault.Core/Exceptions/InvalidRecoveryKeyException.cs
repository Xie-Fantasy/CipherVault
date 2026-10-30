namespace CipherVault.Core.Exceptions;

public class InvalidRecoveryKeyException : Exception
{
    public InvalidRecoveryKeyException() : base("Invalid recovery key.") { }
    public InvalidRecoveryKeyException(string message) : base(message) { }
}
