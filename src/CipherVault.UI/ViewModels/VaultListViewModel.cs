using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CipherVault.Core.Config;
using CipherVault.Core.Interfaces;
using CipherVault.Core.Models;

namespace CipherVault.UI.ViewModels;

/// <summary>
/// ViewModel for the left navigation panel.
/// Manages the vault switcher, group tree, and special views (All / Favorites / Recent).
/// </summary>
public partial class VaultListViewModel : ViewModelBase
{
    private IVaultRepository _repository;
    private bool _suppressVaultSelection;

    /// <summary>
    /// Occurs when the user selects a group or special view, signaling the entry list to refresh.
    /// </summary>
    public event EventHandler<NavigationSelectionChangedEventArgs>? SelectionChanged;

    /// <summary>Occurs when the user selects a different configured vault.</summary>
    public event EventHandler<VaultSelectionRequestedEventArgs>? VaultSelectionRequested;

    public VaultListViewModel(IVaultRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    /// <summary>Rebinds this view model to the currently unlocked vault.</summary>
    public void SetRepository(IVaultRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        Groups.Clear();
        SelectedGroup = null;
        SpecialView = "All";
    }

    #region Properties

    /// <summary>
    /// List of available vault file paths the user can switch between.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<AppConfigVault> _vaults = new();

    /// <summary>
    /// Hierarchical group nodes displayed in the tree view.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<VaultGroup> _groups = new();

    /// <summary>
    /// Currently selected vault file path.
    /// </summary>
    [ObservableProperty]
    private AppConfigVault? _selectedVault;

    /// <summary>
    /// Currently selected group in the tree, or null if a special view is active.
    /// </summary>
    [ObservableProperty]
    private VaultGroup? _selectedGroup;

    /// <summary>
    /// Identifier for the active special view: "All", "Favorites", "Recent", or null when a group is selected.
    /// </summary>
    [ObservableProperty]
    private string? _specialView;

    /// <summary>
    /// Indicates whether the group data is being loaded.
    /// </summary>
    [ObservableProperty]
    private bool _isLoading;

    #endregion

    #region Commands

    /// <summary>
    /// Adds a vault file path to the list. Typically invoked from a file-browse dialog.
    /// </summary>
    /// <param name="vaultPath">The .cvault file path to add.</param>
    [RelayCommand]
    private void AddVault(string vaultPath)
    {
        if (string.IsNullOrWhiteSpace(vaultPath))
            return;

        AddVault(Path.GetFileNameWithoutExtension(vaultPath), vaultPath);
    }

    /// <summary>
    /// Removes a vault from the list. Does not delete the underlying file.
    /// </summary>
    /// <param name="vaultPath">The vault file path to remove.</param>
    [RelayCommand]
    private void RemoveVault(string vaultPath)
    {
        if (string.IsNullOrWhiteSpace(vaultPath))
            return;

        var item = Vaults.FirstOrDefault(v => v.Path.Equals(vaultPath, StringComparison.OrdinalIgnoreCase));
        if (item == null)
            return;

        Vaults.Remove(item);

        if (SelectedVault?.Path.Equals(vaultPath, StringComparison.OrdinalIgnoreCase) == true)
        {
            SelectedVault = Vaults.Count > 0 ? Vaults[0] : null;
        }
    }

    /// <summary>
    /// Creates a new group and adds it to the tree.
    /// </summary>
    /// <param name="groupName">The display name for the new group.</param>
    [RelayCommand]
    private async Task AddGroupAsync(string groupName)
    {
        if (string.IsNullOrWhiteSpace(groupName))
            return;

        await ExecuteWithErrorHandlingAsync(async () =>
        {
            var group = new VaultGroup
            {
                Name = groupName,
                ParentId = SelectedGroup?.Id,
                SortOrder = Groups.Count,
            };

            await _repository.InsertGroupAsync(group);
            if (SelectedGroup == null)
                Groups.Add(group);
            else
                SelectedGroup.Children.Add(group);
        }, "adding group");
    }

    /// <summary>
    /// Deletes the specified group from the tree.
    /// </summary>
    /// <param name="group">The group to delete.</param>
    [RelayCommand]
    private async Task DeleteGroupAsync(VaultGroup group)
    {
        if (group == null)
            return;

        await ExecuteWithErrorHandlingAsync(async () =>
        {
            await _repository.DeleteGroupAsync(group.Id);
            await LoadGroupsAsync();

            if (SelectedGroup?.Id == group.Id)
            {
                SelectedGroup = null;
                SelectSpecialView("All");
            }
        }, "deleting group");
    }

    /// <summary>
    /// Renames the specified group.
    /// </summary>
    /// <param name="newName">The new display name for the group.</param>
    [RelayCommand]
    private async Task RenameGroupAsync(string newName)
    {
        if (string.IsNullOrWhiteSpace(newName) || SelectedGroup == null)
            return;

        await ExecuteWithErrorHandlingAsync(async () =>
        {
            SelectedGroup.Name = newName;
            await _repository.UpdateGroupAsync(SelectedGroup);

            await LoadGroupsAsync();
        }, "renaming group");
    }

    /// <summary>
    /// Selects a group in the tree and clears the special view.
    /// </summary>
    /// <param name="group">The group to select.</param>
    [RelayCommand]
    private void SelectGroup(VaultGroup group)
    {
        SelectedGroup = group;
        SpecialView = null;
        SelectionChanged?.Invoke(this, new NavigationSelectionChangedEventArgs(group, null));
    }

    /// <summary>
    /// Selects a special view (All / Favorites / Recent) and clears the group selection.
    /// </summary>
    /// <param name="viewName">The special view identifier: "All", "Favorites", or "Recent".</param>
    [RelayCommand]
    private void SelectSpecialView(string viewName)
    {
        SelectedGroup = null;
        SpecialView = viewName;
        SelectionChanged?.Invoke(this, new NavigationSelectionChangedEventArgs(null, viewName));
    }

    #endregion

    #region Methods

    /// <summary>
    /// Loads all groups from the repository and populates the tree.
    /// </summary>
    public async Task LoadGroupsAsync()
    {
        IsLoading = true;
        try
        {
            var groups = await _repository.GetGroupsAsync();
            Groups.Clear();
            var byId = groups.ToDictionary(group => group.Id, StringComparer.Ordinal);
            foreach (var group in groups)
                group.Children.Clear();
            foreach (var group in groups)
            {
                if (group.ParentId != null && byId.TryGetValue(group.ParentId, out var parent))
                    parent.Children.Add(group);
                else
                    Groups.Add(group);
            }

            // Default to "All" view if nothing is selected
            if (SelectedGroup == null && SpecialView == null)
            {
                SelectSpecialView("All");
            }
        }
        catch (Exception ex)
        {
            OnError(ex, "loading groups");
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Sets the active vault and reloads groups for that vault.
    /// </summary>
    /// <param name="vaultPath">The vault file path to activate.</param>
    public async Task SetActiveVaultAsync(string vaultPath)
    {
        _suppressVaultSelection = true;
        try
        {
            SelectedVault = Vaults.FirstOrDefault(v =>
                v.Path.Equals(vaultPath, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _suppressVaultSelection = false;
        }
        SelectedGroup = null;
        SpecialView = null;
        await LoadGroupsAsync();
    }

    /// <summary>Adds a named vault choice without duplicating its path.</summary>
    public void AddVault(string name, string vaultPath)
    {
        if (string.IsNullOrWhiteSpace(vaultPath) ||
            Vaults.Any(v => v.Path.Equals(vaultPath, StringComparison.OrdinalIgnoreCase)))
            return;

        Vaults.Add(new AppConfigVault
        {
            Name = string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(vaultPath) : name,
            Path = vaultPath,
        });
    }

    partial void OnSelectedVaultChanged(AppConfigVault? value)
    {
        if (!_suppressVaultSelection && value != null)
            VaultSelectionRequested?.Invoke(this, new VaultSelectionRequestedEventArgs(value.Path));
    }

    #endregion

    protected override void OnError(Exception ex, string errorContext)
    {
        base.OnError(ex, errorContext);
    }
}

public sealed class VaultSelectionRequestedEventArgs : EventArgs
{
    public VaultSelectionRequestedEventArgs(string vaultPath) => VaultPath = vaultPath;
    public string VaultPath { get; }
}

/// <summary>
/// Event arguments for navigation selection changes in the vault list.
/// </summary>
public sealed class NavigationSelectionChangedEventArgs : EventArgs
{
    /// <summary>
    /// The selected group, or null if a special view is active.
    /// </summary>
    public VaultGroup? SelectedGroup { get; }

    /// <summary>
    /// The special view name ("All", "Favorites", "Recent"), or null if a group is selected.
    /// </summary>
    public string? SpecialView { get; }

    public NavigationSelectionChangedEventArgs(VaultGroup? selectedGroup, string? specialView)
    {
        SelectedGroup = selectedGroup;
        SpecialView = specialView;
    }
}
