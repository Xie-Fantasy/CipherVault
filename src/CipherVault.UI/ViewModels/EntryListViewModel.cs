using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CipherVault.Core.Interfaces;
using CipherVault.Core.Models;
using CipherVault.UI.Localization;

namespace CipherVault.UI.ViewModels;

/// <summary>
/// Sort modes for the entry list.
/// </summary>
public enum EntrySortMode
{
    /// <summary>Sort alphabetically by title, A to Z.</summary>
    NameAscending,

    /// <summary>Sort alphabetically by title, Z to A.</summary>
    NameDescending,

    /// <summary>Sort by most recently modified first.</summary>
    ModifiedDescending,

    /// <summary>Sort by oldest modified first.</summary>
    ModifiedAscending,

    /// <summary>Sort by most recently created first.</summary>
    CreatedDescending,

    /// <summary>Sort by oldest created first.</summary>
    CreatedAscending,
}

public sealed record BulkGroupOption(string? Id, string Name);

/// <summary>
/// ViewModel for the middle panel.
/// Displays a filterable, sortable list of vault entries.
/// </summary>
public partial class EntryListViewModel : ViewModelBase
{
    private IVaultRepository _repository;
    private CancellationTokenSource? _searchCts;
    private string? _currentGroupId;
    private string? _currentSpecialView;

    /// <summary>
    /// Occurs when the user selects an entry, signaling the detail panel to load it.
    /// </summary>
    public event EventHandler<VaultEntry?>? EntrySelected;

    /// <summary>Occurs immediately before a newly created entry is selected.</summary>
    public event EventHandler<VaultEntry>? EntryCreated;

    public EntryListViewModel(IVaultRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    /// <summary>Rebinds this view model to the currently unlocked vault.</summary>
    public void SetRepository(IVaultRepository repository)
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = null;
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        Entries.Clear();
        SelectedEntries.Clear();
        AvailableGroups.Clear();
        AvailableTags.Clear();
        IsBulkMode = false;
        SelectedEntry = null;
        FilteredCount = 0;
        _currentGroupId = null;
        _currentSpecialView = null;
    }

    #region Properties

    /// <summary>
    /// The visible entries after filtering and sorting.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<VaultEntry> _entries = new();

    /// <summary>
    /// The currently selected entry in the list.
    /// </summary>
    [ObservableProperty]
    private VaultEntry? _selectedEntry;

    /// <summary>
    /// The search query used to filter entries in real time.
    /// Setting this property triggers a debounced search.
    /// </summary>
    [ObservableProperty]
    private string? _searchQuery;

    /// <summary>
    /// The current sort mode for the entry list.
    /// </summary>
    [ObservableProperty]
    private EntrySortMode _sortMode = EntrySortMode.NameAscending;

    /// <summary>
    /// When true, only favorite entries are displayed.
    /// </summary>
    [ObservableProperty]
    private bool _showFavoritesOnly;

    /// <summary>
    /// Indicates whether the list is currently loading data.
    /// </summary>
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>
    /// Total number of entries matching the current filter (for status bar display).
    /// </summary>
    [ObservableProperty]
    private int _filteredCount;

    /// <summary>
    /// A user-visible message for an entry-list operation that could not be completed.
    /// </summary>
    [ObservableProperty]
    private string? _operationErrorMessage;

    public ObservableCollection<VaultEntry> SelectedEntries { get; } = new();

    public ObservableCollection<BulkGroupOption> AvailableGroups { get; } = new();

    public ObservableCollection<Tag> AvailableTags { get; } = new();

    [ObservableProperty]
    private int _selectedCount;

    [ObservableProperty]
    private bool _hasSelection;

    [ObservableProperty]
    private bool _isBulkMode;

    [ObservableProperty]
    private bool _isTrashView;

    [ObservableProperty]
    private BulkGroupOption? _selectedBulkGroup;

    [ObservableProperty]
    private Tag? _selectedBulkTag;

    public string SortDescription => SortMode switch
    {
        EntrySortMode.ModifiedDescending => "排序：修改时间（从新到旧）",
        EntrySortMode.ModifiedAscending => "排序：修改时间（从旧到新）",
        EntrySortMode.CreatedDescending => "排序：创建时间（从新到旧）",
        EntrySortMode.CreatedAscending => "排序：创建时间（从旧到新）",
        EntrySortMode.NameDescending => "排序：名称（从 Z 到 A）",
        _ => "排序：名称（从 A 到 Z）",
    };

    #endregion

    #region Partial Methods (Property Changed Hooks)

    partial void OnSearchQueryChanged(string? value)
    {
        // Cancel any pending search and start a new one with debounce
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        _ = DebounceSearchAsync(value, token);
    }

    private async Task DebounceSearchAsync(string? value, CancellationToken token)
    {
        try
        {
            await Task.Delay(200, token);

            if (token.IsCancellationRequested)
                return;

            await PerformSearchAsync(value, token);
        }
        catch (OperationCanceledException)
        {
            // Expected when a new search supersedes this one.
        }
    }

    partial void OnSortModeChanged(EntrySortMode value)
    {
        OnPropertyChanged(nameof(SortDescription));
        _ = RefreshAsync();
    }

    partial void OnShowFavoritesOnlyChanged(bool value)
    {
        _ = RefreshAsync();
    }

    partial void OnSelectedEntryChanged(VaultEntry? value)
    {
        EntrySelected?.Invoke(this, value);
    }

    #endregion

    #region Commands

    /// <summary>
    /// Selects an entry from the list.
    /// </summary>
    /// <param name="entry">The entry to select.</param>
    [RelayCommand]
    private void SelectEntry(VaultEntry entry)
    {
        SelectedEntry = entry;
    }

    /// <summary>
    /// Creates a new empty entry and adds it to the list.
    /// The entry is inserted into the repository and then selected for editing.
    /// </summary>
    [RelayCommand]
    private async Task AddEntryAsync()
    {
        OperationErrorMessage = null;
        await ExecuteWithErrorHandlingAsync(async () =>
        {
            var newEntry = new VaultEntry
            {
                Title = L.T("新条目"),
                GroupId = _currentGroupId,
                CredentialType = CredentialType.Login,
            };

            var id = await _repository.InsertEntryAsync(newEntry);
            newEntry = await _repository.GetEntryByIdAsync(id) ?? newEntry;
            Entries.Insert(0, newEntry);
            EntryCreated?.Invoke(this, newEntry);
            SelectedEntry = newEntry;
            FilteredCount = Entries.Count;
        }, "adding entry");
    }

    [RelayCommand]
    private void DismissOperationError()
    {
        OperationErrorMessage = null;
    }

    /// <summary>
    /// Sets the sort mode for the entry list.
    /// </summary>
    /// <param name="mode">The sort mode to apply.</param>
    [RelayCommand]
    private void SetSortMode(EntrySortMode mode)
    {
        SortMode = mode;
    }

    /// <summary>
    /// Toggles the favorites-only filter.
    /// </summary>
    [RelayCommand]
    private void ToggleFavorites()
    {
        ShowFavoritesOnly = !ShowFavoritesOnly;
    }

    [RelayCommand]
    private async Task FavoriteSelectedAsync()
        => await SetSelectedFavoriteAsync(true);

    [RelayCommand]
    private async Task UnfavoriteSelectedAsync()
        => await SetSelectedFavoriteAsync(false);

    [RelayCommand]
    private async Task MoveSelectedAsync()
    {
        VaultEntry[] selected = SelectedEntries.ToArray();
        if (selected.Length == 0 || SelectedBulkGroup == null || IsTrashView)
            return;

        await RunBulkOperationAsync(async () =>
        {
            foreach (VaultEntry entry in selected)
            {
                entry.GroupId = SelectedBulkGroup.Id;
                await _repository.UpdateEntryAsync(entry);
            }
        });
    }

    [RelayCommand]
    private async Task AddTagToSelectedAsync()
    {
        VaultEntry[] selected = SelectedEntries.ToArray();
        Tag? tag = SelectedBulkTag;
        if (selected.Length == 0 || tag == null || IsTrashView)
            return;

        await RunBulkOperationAsync(async () =>
        {
            foreach (VaultEntry entry in selected)
            {
                string[] tagIds = entry.TagIds
                    .Append(tag.Id)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                await _repository.SetEntryTagsAsync(entry.Id, tagIds);
                entry.TagIds = tagIds.ToList();
            }
        });
    }

    [RelayCommand]
    private async Task DeleteSelectedAsync()
    {
        VaultEntry[] selected = SelectedEntries.ToArray();
        if (selected.Length == 0 || IsTrashView)
            return;

        await RunBulkOperationAsync(async () =>
        {
            foreach (VaultEntry entry in selected)
                await _repository.SoftDeleteEntryAsync(entry.Id);
        });
    }

    [RelayCommand]
    private async Task RestoreSelectedAsync()
    {
        VaultEntry[] selected = SelectedEntries.ToArray();
        if (selected.Length == 0 || !IsTrashView)
            return;

        await RunBulkOperationAsync(async () =>
        {
            foreach (VaultEntry entry in selected)
                await _repository.RestoreEntryAsync(entry.Id);
        });
    }

    [RelayCommand]
    private async Task PermanentlyDeleteSelectedAsync()
    {
        VaultEntry[] selected = SelectedEntries.ToArray();
        if (selected.Length == 0 || !IsTrashView)
            return;

        await RunBulkOperationAsync(async () =>
        {
            foreach (VaultEntry entry in selected)
                await _repository.HardDeleteEntryAsync(entry.Id);
        });
    }

    #endregion

    #region Methods

    /// <summary>
    /// Loads entries for the specified group or special view.
    /// </summary>
    /// <param name="groupId">The group ID to filter by, or null for all entries.</param>
    public async Task LoadEntriesAsync(string? groupId = null)
    {
        _currentGroupId = groupId;
        _currentSpecialView = null;
        IsTrashView = false;
        IsLoading = true;

        try
        {
            var loaded = await _repository.GetEntriesAsync(groupId, includeDeleted: false);
            ApplyFilterAndSort(loaded);
            await LoadBulkOptionsAsync();
        }
        catch (Exception ex)
        {
            OnError(ex, "loading entries");
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Loads entries for a special view (All, Favorites, Recent).
    /// </summary>
    /// <param name="specialView">The special view identifier.</param>
    public async Task LoadSpecialViewAsync(string specialView)
    {
        _currentSpecialView = specialView;
        _currentGroupId = null;
        IsTrashView = specialView == "Trash";
        IsLoading = true;

        try
        {
            var loaded = specialView switch
            {
                "Favorites" => await _repository.GetFavoriteEntriesAsync(),
                "Recent" => await _repository.GetRecentEntriesAsync(20),
                "Trash" => (await _repository.GetEntriesAsync(includeDeleted: true))
                    .Where(entry => entry.DeletedAt.HasValue)
                    .ToList(),
                _ => await _repository.GetEntriesAsync(includeDeleted: false), // "All"
            };

            ApplyFilterAndSort(loaded);
            await LoadBulkOptionsAsync();
        }
        catch (Exception ex)
        {
            OnError(ex, "loading special view");
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Refreshes the entry list using the current group/special view and filters.
    /// </summary>
    public async Task RefreshAsync()
    {
        if (_currentSpecialView != null)
        {
            await LoadSpecialViewAsync(_currentSpecialView);
        }
        else
        {
            await LoadEntriesAsync(_currentGroupId);
        }
    }

    /// <summary>
    /// Removes an entry from the visible list (after soft delete).
    /// </summary>
    /// <param name="entryId">The ID of the entry to remove from the list.</param>
    public void RemoveEntryFromList(string entryId)
    {
        var entry = Entries.FirstOrDefault(e => e.Id == entryId);
        if (entry != null)
        {
            Entries.Remove(entry);
            FilteredCount = Entries.Count;

            if (SelectedEntry?.Id == entryId)
            {
                SelectedEntry = Entries.Count > 0 ? Entries[0] : null;
            }
        }
    }

    /// <summary>
    /// Updates an entry in the visible list (after save).
    /// </summary>
    /// <param name="updatedEntry">The updated entry.</param>
    public void UpdateEntryInList(VaultEntry updatedEntry)
    {
        var index = Entries.ToList().FindIndex(e => e.Id == updatedEntry.Id);
        if (index >= 0)
        {
            Entries[index] = updatedEntry;
        }
    }

    public void UpdateSelection(IEnumerable<VaultEntry> selectedEntries)
    {
        string[] selectedIds = selectedEntries.Select(entry => entry.Id).ToArray();
        SelectedEntries.Clear();
        foreach (string id in selectedIds)
        {
            VaultEntry? entry = Entries.FirstOrDefault(candidate => candidate.Id == id);
            if (entry != null)
                SelectedEntries.Add(entry);
        }

        SelectedCount = SelectedEntries.Count;
        HasSelection = SelectedCount > 0;
    }

    #endregion

    #region Private Helpers

    private async Task PerformSearchAsync(string? query, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            if (_currentSpecialView != null)
                await LoadSpecialViewAsync(_currentSpecialView);
            else
                await LoadEntriesAsync(_currentGroupId);
            return;
        }

        try
        {
            var results = _currentSpecialView == "Trash"
                ? (await _repository.GetEntriesAsync(includeDeleted: true))
                    .Where(entry => entry.DeletedAt.HasValue && MatchesQuery(entry, query))
                    .ToList()
                : await _repository.SearchEntriesAsync(query);

            if (token.IsCancellationRequested)
                return;

            ApplyFilterAndSort(results);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            OnError(ex, "searching entries");
        }
    }

    private void ApplyFilterAndSort(System.Collections.Generic.List<VaultEntry> loaded)
    {
        string? selectedId = SelectedEntry?.Id;
        var filtered = loaded.AsEnumerable();

        // Apply favorites filter
        if (ShowFavoritesOnly)
        {
            filtered = filtered.Where(e => e.IsFavorite);
        }

        // Apply sorting
        var sorted = SortMode switch
        {
            EntrySortMode.NameAscending => filtered
                .OrderBy(e => e.Title, StringComparer.OrdinalIgnoreCase)
                .ThenByDescending(e => e.ModifiedAt)
                .ToList(),
            EntrySortMode.NameDescending => filtered
                .OrderByDescending(e => e.Title, StringComparer.OrdinalIgnoreCase)
                .ThenByDescending(e => e.ModifiedAt)
                .ToList(),
            EntrySortMode.ModifiedDescending => filtered
                .OrderByDescending(e => e.ModifiedAt)
                .ThenBy(e => e.Title, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            EntrySortMode.ModifiedAscending => filtered
                .OrderBy(e => e.ModifiedAt)
                .ThenBy(e => e.Title, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            EntrySortMode.CreatedDescending => filtered
                .OrderByDescending(e => e.CreatedAt)
                .ThenBy(e => e.Title, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            EntrySortMode.CreatedAscending => filtered
                .OrderBy(e => e.CreatedAt)
                .ThenBy(e => e.Title, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            _ => filtered.ToList(),
        };

        Entries.Clear();
        foreach (var entry in sorted)
        {
            Entries.Add(entry);
        }

        FilteredCount = Entries.Count;
        UpdateSelection(Array.Empty<VaultEntry>());

        SelectedEntry = selectedId == null
            ? Entries.FirstOrDefault()
            : Entries.FirstOrDefault(entry => entry.Id == selectedId) ?? Entries.FirstOrDefault();
    }

    private static bool MatchesQuery(VaultEntry entry, string query)
    {
        return entry.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               (entry.Username?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
               (entry.Url?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
               (entry.Notes?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private async Task SetSelectedFavoriteAsync(bool favorite)
    {
        VaultEntry[] selected = SelectedEntries.ToArray();
        if (selected.Length == 0 || IsTrashView)
            return;

        await RunBulkOperationAsync(async () =>
        {
            foreach (VaultEntry entry in selected)
            {
                entry.IsFavorite = favorite;
                await _repository.UpdateEntryAsync(entry);
            }
        });
    }

    private async Task RunBulkOperationAsync(Func<Task> operation)
    {
        OperationErrorMessage = null;
        IsLoading = true;
        try
        {
            await _repository.ExecuteTransactionAsync(operation);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            OnError(ex, "bulk editing entries");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadBulkOptionsAsync()
    {
        List<VaultGroup> groups = await _repository.GetGroupsAsync();
        List<Tag> tags = await _repository.GetTagsAsync();

        string? selectedGroupId = SelectedBulkGroup?.Id;
        string? selectedTagId = SelectedBulkTag?.Id;

        AvailableGroups.Clear();
        AvailableGroups.Add(new BulkGroupOption(null, L.T("未分组")));
        foreach (VaultGroup group in groups.OrderBy(group => group.SortOrder).ThenBy(group => group.Name))
            AvailableGroups.Add(new BulkGroupOption(group.Id, group.Name));

        AvailableTags.Clear();
        foreach (Tag tag in tags.OrderBy(tag => tag.Name))
            AvailableTags.Add(tag);

        SelectedBulkGroup = AvailableGroups.FirstOrDefault(group => group.Id == selectedGroupId)
            ?? AvailableGroups.FirstOrDefault();
        SelectedBulkTag = AvailableTags.FirstOrDefault(tag => tag.Id == selectedTagId)
            ?? AvailableTags.FirstOrDefault();
    }

    #endregion

    protected override void OnError(Exception ex, string errorContext)
    {
        base.OnError(ex, errorContext);
        if (errorContext is "adding entry" or "bulk editing entries")
            OperationErrorMessage = L.F("操作失败：{0}", ex.Message);
    }
}
