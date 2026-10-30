namespace CipherVault.Core.Models;

public sealed class RecoveryKey
{
    public string[] Words { get; init; } = Array.Empty<string>();
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public string ToMnemonicString() => string.Join(" ", Words);

    public static RecoveryKey FromMnemonicString(string mnemonic)
    {
        var words = mnemonic.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return new RecoveryKey { Words = words };
    }
}
