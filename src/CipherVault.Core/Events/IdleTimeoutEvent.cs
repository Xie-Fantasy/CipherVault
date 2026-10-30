namespace CipherVault.Core.Events;

public sealed class IdleTimeoutEvent
{
    public int IdleSeconds { get; init; }
}
