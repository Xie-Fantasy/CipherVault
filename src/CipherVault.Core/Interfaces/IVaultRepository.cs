using CipherVault.Core.Models;

namespace CipherVault.Core.Interfaces;

public interface IVaultRepository
{
    /// <summary>
    /// Executes related repository operations as one SQLite transaction and emits
    /// at most one persistence notification after a successful commit.
    /// </summary>
    Task ExecuteTransactionAsync(Func<Task> operation);

    // Entries
    Task<List<VaultEntry>> GetEntriesAsync(string? groupId = null, bool includeDeleted = false);
    Task<VaultEntry?> GetEntryByIdAsync(string id);
    Task<List<VaultEntry>> SearchEntriesAsync(string query);
    Task<List<VaultEntry>> GetFavoriteEntriesAsync();
    Task<List<VaultEntry>> GetRecentEntriesAsync(int count = 20);
    Task<string> InsertEntryAsync(VaultEntry entry);
    Task UpdateEntryAsync(VaultEntry entry);
    Task MarkEntryAccessedAsync(string id, DateTime accessedAt);
    Task SoftDeleteEntryAsync(string id);
    Task RestoreEntryAsync(string id);
    Task HardDeleteEntryAsync(string id);
    Task PurgeDeletedEntriesAsync(int olderThanDays = 30);

    // Groups
    Task<List<VaultGroup>> GetGroupsAsync();
    Task<string> InsertGroupAsync(VaultGroup group);
    Task UpdateGroupAsync(VaultGroup group);
    Task DeleteGroupAsync(string id);

    // Tags
    Task<List<Tag>> GetTagsAsync();
    Task<List<Tag>> GetTagsForEntryAsync(string entryId);
    Task<string> InsertTagAsync(Tag tag);
    Task DeleteTagAsync(string id);
    Task SetEntryTagsAsync(string entryId, IEnumerable<string> tagIds);

    // History
    Task<List<EntryHistory>> GetEntryHistoryAsync(string entryId);
    Task InsertHistoryAsync(EntryHistory history);
}
