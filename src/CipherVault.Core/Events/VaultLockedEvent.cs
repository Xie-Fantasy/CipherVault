using CipherVault.Core.Enums;

namespace CipherVault.Core.Events;

public sealed class VaultLockedEvent
{
    public LockReason Reason { get; init; }
    public string? VaultFilePath { get; init; }
}
