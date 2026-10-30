namespace CipherVault.Core.Models;

public sealed class EntryHistory
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string EntryId { get; set; } = string.Empty;
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public string SnapshotJson { get; set; } = string.Empty;
    public string? ChangeDescription { get; set; }
}
