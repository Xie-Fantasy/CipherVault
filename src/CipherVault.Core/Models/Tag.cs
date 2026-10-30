namespace CipherVault.Core.Models;

public sealed class Tag
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string? Color { get; set; }
}
