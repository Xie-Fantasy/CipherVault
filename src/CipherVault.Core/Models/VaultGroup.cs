namespace CipherVault.Core.Models;

using System.Collections.ObjectModel;

public sealed class VaultGroup
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string? ParentId { get; set; }
    public int SortOrder { get; set; }
    public string? IconName { get; set; }
    public ObservableCollection<VaultGroup> Children { get; } = new();
}
