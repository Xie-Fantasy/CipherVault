namespace CipherVault.Core.Exceptions;

public class InvalidMasterPasswordException : Exception
{
    public int AttemptsRemaining { get; init; }

    public InvalidMasterPasswordException(int attemptsRemaining)
        : base($"Invalid master password. {attemptsRemaining} attempts remaining.")
    {
        AttemptsRemaining = attemptsRemaining;
    }
}
