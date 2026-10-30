namespace CipherVault.Core.Events;

public sealed class VaultUnlockedEvent
{
    public string VaultFilePath { get; init; } = string.Empty;
}
