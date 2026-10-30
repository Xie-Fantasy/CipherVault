namespace CipherVault.Core.Models;

public sealed class CustomField
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public bool IsSensitive { get; set; }
}
