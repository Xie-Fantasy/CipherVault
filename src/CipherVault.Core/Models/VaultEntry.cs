namespace CipherVault.Core.Models;

public sealed class VaultEntry
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = string.Empty;
    public CredentialType CredentialType { get; set; } = CredentialType.Login;
    public string? Username { get; set; }
    public byte[]? PasswordEncrypted { get; set; }
    public string? Url { get; set; }
    public string? Notes { get; set; }
    public string? TotpSecret { get; set; }
    public bool IsFavorite { get; set; }
    public string? GroupId { get; set; }
    public List<CustomField> CustomFields { get; set; } = new();
    public List<string> TagIds { get; set; } = new();
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastAccessedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}
