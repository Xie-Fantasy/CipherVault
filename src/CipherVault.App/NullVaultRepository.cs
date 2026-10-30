using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CipherVault.Core.Interfaces;
using CipherVault.Core.Models;

namespace CipherVault.App;

/// <summary>
/// A null implementation of IVaultRepository that throws NotSupportedException for all operations.
/// Used as a placeholder before a vault is unlocked.
/// </summary>
internal sealed class NullVaultRepository : IVaultRepository
{
    private const string ErrorMessage = "No vault is currently unlocked. Please unlock a vault first.";

    public Task ExecuteTransactionAsync(Func<Task> operation)
        => throw new InvalidOperationException(ErrorMessage);

    public Task<List<VaultEntry>> GetEntriesAsync(string? groupId = null, bool includeDeleted = false)
        => throw new InvalidOperationException(ErrorMessage);

    public Task<VaultEntry?> GetEntryByIdAsync(string id)
        => throw new InvalidOperationException(ErrorMessage);

    public Task<List<VaultEntry>> GetFavoriteEntriesAsync()
        => throw new InvalidOperationException(ErrorMessage);

    public Task<List<VaultEntry>> GetRecentEntriesAsync(int count = 20)
        => throw new InvalidOperationException(ErrorMessage);

    public Task<string> InsertEntryAsync(VaultEntry entry)
        => throw new InvalidOperationException(ErrorMessage);

    public Task UpdateEntryAsync(VaultEntry entry)
        => throw new InvalidOperationException(ErrorMessage);

    public Task MarkEntryAccessedAsync(string id, DateTime accessedAt)
        => throw new InvalidOperationException(ErrorMessage);

    public Task SoftDeleteEntryAsync(string id)
        => throw new InvalidOperationException(ErrorMessage);

    public Task RestoreEntryAsync(string id)
        => throw new InvalidOperationException(ErrorMessage);

    public Task HardDeleteEntryAsync(string id)
        => throw new InvalidOperationException(ErrorMessage);

    public Task PurgeDeletedEntriesAsync(int olderThanDays = 30)
        => throw new InvalidOperationException(ErrorMessage);

    public Task<List<VaultEntry>> SearchEntriesAsync(string query)
        => throw new InvalidOperationException(ErrorMessage);

    public Task<List<VaultGroup>> GetGroupsAsync()
        => throw new InvalidOperationException(ErrorMessage);

    public Task<string> InsertGroupAsync(VaultGroup group)
        => throw new InvalidOperationException(ErrorMessage);

    public Task UpdateGroupAsync(VaultGroup group)
        => throw new InvalidOperationException(ErrorMessage);

    public Task DeleteGroupAsync(string id)
        => throw new InvalidOperationException(ErrorMessage);

    public Task<List<EntryHistory>> GetEntryHistoryAsync(string entryId)
        => throw new InvalidOperationException(ErrorMessage);

    public Task InsertHistoryAsync(EntryHistory history)
        => throw new InvalidOperationException(ErrorMessage);

    public Task<List<Tag>> GetTagsForEntryAsync(string entryId)
        => throw new InvalidOperationException(ErrorMessage);

    public Task<List<Tag>> GetTagsAsync()
        => throw new InvalidOperationException(ErrorMessage);

    public Task<string> InsertTagAsync(Tag tag)
        => throw new InvalidOperationException(ErrorMessage);

    public Task DeleteTagAsync(string id)
        => throw new InvalidOperationException(ErrorMessage);

    public Task SetEntryTagsAsync(string entryId, IEnumerable<string> tagIds)
        => throw new InvalidOperationException(ErrorMessage);
}
