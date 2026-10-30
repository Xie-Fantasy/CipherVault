namespace CipherVault.Core.Exceptions;

public class ConcurrentAccessException : Exception
{
    public ConcurrentAccessException()
        : base("The vault is already opened in another instance.") { }
    public ConcurrentAccessException(string message) : base(message) { }
}
