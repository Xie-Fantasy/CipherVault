namespace CipherVault.UI.Tests.ViewModels;

using CipherVault.Core.Interfaces;
using CipherVault.Core.Models;
using CipherVault.Infrastructure.Crypto;
using CipherVault.Infrastructure.Database;
using CipherVault.Infrastructure.Security;
using CipherVault.Infrastructure.Vault;
using CipherVault.UI.ViewModels;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

public sealed class VaultAndEntryViewModelTests
{
    [Fact]
    public async Task EntryList_SearchQueryFiltersVisibleEntries()
    {
        using var fixture = new RepositoryFixture();
        await fixture.Repository.InsertEntryAsync(new VaultEntry
        {
            Title = "新条目",
            Username = "new-user",
        });
        await fixture.Repository.InsertEntryAsync(new VaultEntry
        {
            Title = "GitHub",
            Username = "developer",
        });
        var sut = new EntryListViewModel(fixture.Repository);
        await sut.LoadSpecialViewAsync("All");

        sut.SearchQuery = "新";
        await Task.Delay(350);

        sut.Entries.Should().ContainSingle();
        sut.Entries[0].Title.Should().Be("新条目");
        sut.FilteredCount.Should().Be(1);
    }

    [Fact]
    public async Task EntryList_SupportsExplicitNameCreatedAndModifiedSortDirections()
    {
        using var fixture = new RepositoryFixture();
        DateTime baseline = new(2026, 7, 27, 8, 0, 0, DateTimeKind.Utc);
        await fixture.Repository.InsertEntryAsync(new VaultEntry
        {
            Title = "Bravo",
            CreatedAt = baseline.AddHours(2),
            ModifiedAt = baseline.AddHours(1),
        });
        await fixture.Repository.InsertEntryAsync(new VaultEntry
        {
            Title = "Alpha",
            CreatedAt = baseline,
            ModifiedAt = baseline.AddHours(2),
        });
        await fixture.Repository.InsertEntryAsync(new VaultEntry
        {
            Title = "Charlie",
            CreatedAt = baseline.AddHours(1),
            ModifiedAt = baseline,
        });
        var sut = new EntryListViewModel(fixture.Repository);
        await sut.LoadSpecialViewAsync("All");

        var expectations = new[]
        {
            (EntrySortMode.NameAscending, new[] { "Alpha", "Bravo", "Charlie" }),
            (EntrySortMode.NameDescending, new[] { "Charlie", "Bravo", "Alpha" }),
            (EntrySortMode.CreatedDescending, new[] { "Bravo", "Charlie", "Alpha" }),
            (EntrySortMode.CreatedAscending, new[] { "Alpha", "Charlie", "Bravo" }),
            (EntrySortMode.ModifiedDescending, new[] { "Alpha", "Bravo", "Charlie" }),
            (EntrySortMode.ModifiedAscending, new[] { "Charlie", "Bravo", "Alpha" }),
        };

        foreach ((EntrySortMode mode, string[] expected) in expectations)
        {
            sut.SortMode = mode;
            await sut.RefreshAsync();
            sut.Entries.Select(entry => entry.Title).Should().Equal(expected);
        }
    }

    [Fact]
    public async Task EntryListRaisesCreatedBeforeSelectingNewEntry()
    {
        using var fixture = new RepositoryFixture();
        var sut = new EntryListViewModel(fixture.Repository);
        var sequence = new List<string>();
        sut.EntryCreated += (_, _) => sequence.Add("created");
        sut.EntrySelected += (_, _) => sequence.Add("selected");

        await sut.AddEntryCommand.ExecuteAsync(null);

        sequence.Should().Equal("created", "selected");
        sut.SelectedEntry.Should().NotBeNull();
        (await fixture.Repository.GetEntriesAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task TrashViewOnlyShowsDeletedEntriesAndRestoreReturnsEntryToAll()
    {
        using var fixture = new RepositoryFixture();
        var active = new VaultEntry { Title = "保留" };
        var deleted = new VaultEntry { Title = "误删" };
        await fixture.Repository.InsertEntryAsync(active);
        await fixture.Repository.InsertEntryAsync(deleted);
        await fixture.Repository.SoftDeleteEntryAsync(deleted.Id);
        var list = new EntryListViewModel(fixture.Repository);

        await list.LoadSpecialViewAsync("Trash");

        list.Entries.Should().ContainSingle(item => item.Id == deleted.Id);
        using var secureMemory = new SecureMemoryManager();
        using var keyManager = new VaultKeyManager(secureMemory);
        var detail = new EntryDetailViewModel(
            fixture.Repository,
            new RecordingClipboardGuard(),
            new CryptoService(),
            keyManager);
        await detail.LoadEntryAsync(deleted.Id);
        detail.IsCurrentEntryDeleted.Should().BeTrue();

        await detail.RestoreDeletedCommand.ExecuteAsync(null);

        detail.IsCurrentEntryDeleted.Should().BeFalse();
        (await fixture.Repository.GetEntriesAsync()).Should().HaveCount(2);
    }

    [Fact]
    public async Task EntryList_BulkOperationsMoveTagFavoriteDeleteAndRestoreAsAGroup()
    {
        using var fixture = new RepositoryFixture();
        var first = new VaultEntry { Title = "First" };
        var second = new VaultEntry { Title = "Second" };
        var group = new VaultGroup { Name = "Work" };
        var tag = new Tag { Name = "Important" };
        await fixture.Repository.InsertEntryAsync(first);
        await fixture.Repository.InsertEntryAsync(second);
        await fixture.Repository.InsertGroupAsync(group);
        await fixture.Repository.InsertTagAsync(tag);

        var sut = new EntryListViewModel(fixture.Repository);
        await sut.LoadSpecialViewAsync("All");
        sut.IsBulkMode = true;
        sut.UpdateSelection(sut.Entries.Take(1));
        sut.HasSelection.Should().BeTrue();
        sut.SelectedCount.Should().Be(1);

        sut.UpdateSelection(sut.Entries);
        await sut.FavoriteSelectedCommand.ExecuteAsync(null);

        (await fixture.Repository.GetEntriesAsync()).Should().OnlyContain(entry => entry.IsFavorite);

        sut.UpdateSelection(sut.Entries);
        sut.SelectedBulkGroup = sut.AvailableGroups.Single(option => option.Id == group.Id);
        await sut.MoveSelectedCommand.ExecuteAsync(null);

        (await fixture.Repository.GetEntriesAsync()).Should().OnlyContain(entry => entry.GroupId == group.Id);

        sut.UpdateSelection(sut.Entries);
        sut.SelectedBulkTag = sut.AvailableTags.Single(item => item.Id == tag.Id);
        await sut.AddTagToSelectedCommand.ExecuteAsync(null);

        (await fixture.Repository.GetTagsForEntryAsync(first.Id)).Should().ContainSingle(item => item.Id == tag.Id);
        (await fixture.Repository.GetTagsForEntryAsync(second.Id)).Should().ContainSingle(item => item.Id == tag.Id);

        sut.UpdateSelection(sut.Entries);
        await sut.DeleteSelectedCommand.ExecuteAsync(null);
        (await fixture.Repository.GetEntriesAsync()).Should().BeEmpty();

        await sut.LoadSpecialViewAsync("Trash");
        sut.UpdateSelection(sut.Entries);
        await sut.RestoreSelectedCommand.ExecuteAsync(null);

        (await fixture.Repository.GetEntriesAsync()).Should().HaveCount(2);
    }

    [Fact]
    public async Task VaultList_LoadsHierarchyAndAddsChildToSelectedGroup()
    {
        using var fixture = new RepositoryFixture();
        var parent = new VaultGroup { Name = "工作" };
        var child = new VaultGroup { Name = "服务器", ParentId = parent.Id };
        await fixture.Repository.InsertGroupAsync(parent);
        await fixture.Repository.InsertGroupAsync(child);
        var groupedEntry = new VaultEntry { Title = "内部系统", GroupId = parent.Id };
        await fixture.Repository.InsertEntryAsync(groupedEntry);
        var sut = new VaultListViewModel(fixture.Repository);

        await sut.LoadGroupsAsync();

        sut.Groups.Should().ContainSingle();
        sut.Groups[0].Children.Should().ContainSingle(item => item.Id == child.Id);

        VaultGroup loadedParent = sut.Groups[0];
        sut.SelectGroupCommand.Execute(loadedParent);
        await sut.AddGroupCommand.ExecuteAsync("云服务");

        loadedParent.Children.Should().Contain(item => item.Name == "云服务");
        (await fixture.Repository.GetGroupsAsync()).Should().HaveCount(3);

        await sut.DeleteGroupCommand.ExecuteAsync(loadedParent);

        (await fixture.Repository.GetEntryByIdAsync(groupedEntry.Id))!.GroupId.Should().BeNull();
        (await fixture.Repository.GetGroupsAsync()).Single(item => item.Id == child.Id).ParentId.Should().BeNull();
    }

    [Fact]
    public async Task EntryDetail_SavePersistsCustomFieldsAndTags_AndCancelRestoresTags()
    {
        using var fixture = new RepositoryFixture();
        var entry = new VaultEntry
        {
            Title = "GitHub",
            CustomFields = [new CustomField { Key = "环境", Value = "生产" }],
        };
        await fixture.Repository.InsertEntryAsync(entry);
        var originalTag = new Tag { Name = "开发" };
        await fixture.Repository.InsertTagAsync(originalTag);
        await fixture.Repository.SetEntryTagsAsync(entry.Id, [originalTag.Id]);

        using var secureMemory = new SecureMemoryManager();
        using var keyManager = new VaultKeyManager(secureMemory);
        var sut = new EntryDetailViewModel(
            fixture.Repository,
            new RecordingClipboardGuard(),
            new CryptoService(),
            keyManager);

        await sut.LoadEntryAsync(entry.Id);
        await sut.ToggleEditCommand.ExecuteAsync(null);
        sut.AddCustomFieldCommand.Execute(null);
        sut.EditCustomFields[^1].Key = "区域";
        sut.EditCustomFields[^1].Value = "华东";
        sut.AddTagCommand.Execute("主账号");

        await sut.SaveCommand.ExecuteAsync(null);

        VaultEntry saved = (await fixture.Repository.GetEntryByIdAsync(entry.Id))!;
        saved.CustomFields.Should().Contain(item => item.Key == "区域" && item.Value == "华东");
        (await fixture.Repository.GetTagsForEntryAsync(entry.Id))
            .Should().Contain(item => item.Name == "主账号");

        await sut.ToggleEditCommand.ExecuteAsync(null);
        Tag tagToRemove = sut.Tags.Single(item => item.Name == "主账号");
        sut.RemoveTagCommand.Execute(tagToRemove);
        await sut.CancelEditCommand.ExecuteAsync(null);

        sut.Tags.Should().Contain(item => item.Name == "主账号");
    }

    [Fact]
    public async Task EntryDetail_EditBufferDoesNotMutateEntryUntilSave_AndCancelDropsChanges()
    {
        using var fixture = new RepositoryFixture();
        var entry = new VaultEntry { Title = "原名称", Username = "original" };
        await fixture.Repository.InsertEntryAsync(entry);

        using var secureMemory = new SecureMemoryManager();
        using var keyManager = new VaultKeyManager(secureMemory);
        var sut = new EntryDetailViewModel(
            fixture.Repository,
            new RecordingClipboardGuard(),
            new CryptoService(),
            keyManager);

        await sut.LoadEntryAsync(entry.Id);
        await sut.ToggleEditCommand.ExecuteAsync(null);
        sut.EditTitle = "未保存名称";
        sut.EditUsername = "changed";

        sut.CurrentEntry!.Title.Should().Be("原名称");
        sut.CurrentEntry.Username.Should().Be("original");

        await sut.CancelEditCommand.ExecuteAsync(null);

        sut.IsEditing.Should().BeFalse();
        sut.CurrentEntry.Title.Should().Be("原名称");
        sut.EditTitle.Should().Be("原名称");
    }

    [Fact]
    public async Task EntryDetail_SavesOtherCredentialType()
    {
        using var fixture = new RepositoryFixture();
        var entry = new VaultEntry { Title = "图标测试" };
        await fixture.Repository.InsertEntryAsync(entry);

        using var secureMemory = new SecureMemoryManager();
        using var keyManager = new VaultKeyManager(secureMemory);
        var sut = new EntryDetailViewModel(
            fixture.Repository,
            new RecordingClipboardGuard(),
            new CryptoService(),
            keyManager);

        await sut.LoadEntryAsync(entry.Id);
        await sut.ToggleEditCommand.ExecuteAsync(null);
        sut.EditCredentialType = CredentialType.Custom;

        await sut.SaveCommand.ExecuteAsync(null);

        VaultEntry saved = (await fixture.Repository.GetEntryByIdAsync(entry.Id))!;
        saved.CredentialType.Should().Be(CredentialType.Custom);
    }

    [Fact]
    public async Task EntryDetail_CanSetAndClearPassword_AndCopiesUrlThroughGuard()
    {
        using var fixture = new RepositoryFixture();
        var entry = new VaultEntry { Title = "站点", Url = "https://example.com" };
        await fixture.Repository.InsertEntryAsync(entry);

        using var secureMemory = new SecureMemoryManager();
        using var keyManager = new VaultKeyManager(secureMemory);
        keyManager.StoreKey(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());
        var clipboard = new RecordingClipboardGuard();
        var sut = new EntryDetailViewModel(
            fixture.Repository,
            clipboard,
            new CryptoService(),
            keyManager);

        await sut.LoadEntryAsync(entry.Id);
        await sut.ToggleEditCommand.ExecuteAsync(null);
        sut.EditPassword = "secret-value";
        await sut.SaveCommand.ExecuteAsync(null);
        sut.CurrentEntry!.PasswordEncrypted.Should().NotBeNullOrEmpty();
        sut.EditPassword.Should().BeNull();

        await sut.ToggleEditCommand.ExecuteAsync(null);
        sut.EditPassword.Should().Be("secret-value");
        sut.EditPassword = string.Empty;
        await sut.SaveCommand.ExecuteAsync(null);
        sut.CurrentEntry.PasswordEncrypted.Should().BeNull();

        await sut.CopyUrlCommand.ExecuteAsync(null);
        clipboard.LastText.Should().Be("https://example.com");
        clipboard.LastAutoClear.Should().BeTrue();
    }

    [Fact]
    public async Task EntryDetail_HistoryRestoreRestoresFavoriteGroupCustomFieldsAndTags()
    {
        using var fixture = new RepositoryFixture();
        var group = new VaultGroup { Name = "工作" };
        await fixture.Repository.InsertGroupAsync(group);
        var originalTag = new Tag { Name = "原标签" };
        var newTag = new Tag { Name = "新标签" };
        await fixture.Repository.InsertTagAsync(originalTag);
        await fixture.Repository.InsertTagAsync(newTag);
        var entry = new VaultEntry
        {
            Title = "原版本",
            GroupId = group.Id,
            IsFavorite = true,
            CustomFields = [new CustomField { Key = "环境", Value = "生产" }],
        };
        await fixture.Repository.InsertEntryAsync(entry);
        await fixture.Repository.SetEntryTagsAsync(entry.Id, [originalTag.Id]);

        using var secureMemory = new SecureMemoryManager();
        using var keyManager = new VaultKeyManager(secureMemory);
        var sut = new EntryDetailViewModel(
            fixture.Repository,
            new RecordingClipboardGuard(),
            new CryptoService(),
            keyManager);
        await sut.LoadEntryAsync(entry.Id);
        await sut.ToggleEditCommand.ExecuteAsync(null);
        sut.EditTitle = "新版本";
        sut.EditCustomFields[0].Value = "测试";
        sut.Tags.Clear();
        sut.Tags.Add(newTag);
        await sut.SaveCommand.ExecuteAsync(null);
        await sut.ToggleFavoriteCommand.ExecuteAsync(null);
        sut.CurrentEntry!.GroupId = null;
        await fixture.Repository.UpdateEntryAsync(sut.CurrentEntry);

        await sut.LoadHistoryAsync();
        await sut.RestoreFromHistoryCommand.ExecuteAsync(sut.HistoryEntries.Last());

        sut.CurrentEntry!.Title.Should().Be("原版本");
        sut.CurrentEntry.IsFavorite.Should().BeTrue();
        sut.CurrentEntry.GroupId.Should().Be(group.Id);
        sut.CurrentEntry.CustomFields.Should().ContainSingle(field => field.Value == "生产");
        (await fixture.Repository.GetTagsForEntryAsync(entry.Id))
            .Should().ContainSingle(tag => tag.Id == originalTag.Id);
    }

    private sealed class RepositoryFixture : IDisposable
    {
        private readonly SqliteConnection _connection = new("Data Source=:memory:");
        public VaultRepository Repository { get; }

        public RepositoryFixture()
        {
            _connection.Open();
            SchemaMigrator.InitializeSchema(_connection);
            Repository = new VaultRepository(_connection);
        }

        public void Dispose() => Repository.Dispose();
    }

    private sealed class RecordingClipboardGuard : IClipboardGuard
    {
        public int AutoClearSeconds { get; set; } = 15;
        public string? LastText { get; private set; }
        public bool LastAutoClear { get; private set; }
        public event Action? ClipboardCleared;
        public void CopyToClipboard(string text, bool autoClear = true)
        {
            LastText = text;
            LastAutoClear = autoClear;
        }
        public void ClearClipboard() => ClipboardCleared?.Invoke();
    }
}
