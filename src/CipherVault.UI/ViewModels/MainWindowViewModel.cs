using System;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CipherVault.Core.Enums;
using CipherVault.Core.Events;
using CipherVault.Core.Interfaces;
using CipherVault.Core.Models;
using CipherVault.UI.Localization;
using Microsoft.Data.Sqlite;

namespace CipherVault.UI.ViewModels;

/// <summary>
/// ViewModel for the main application window.
/// Coordinates the three-panel layout, vault state transitions, and global commands.
/// </summary>
public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IEventBus _eventBus;
    private readonly IBackupService _backupService;
    private readonly IGlobalHotkeyService _globalHotkeyService;
    private string? _entryIdToEditAfterSelection;

    /// <summary>
    /// Occurs when the user requests to open the settings dialog.
    /// </summary>
    public event EventHandler? SettingsRequested;

    /// <summary>
    /// Occurs when the user requests to open the global search popup.
    /// </summary>
    public event EventHandler? GlobalSearchRequested;

    /// <summary>
    /// Occurs when the user requests to open the setup wizard.
    /// </summary>
    public event EventHandler? SetupWizardRequested;

    /// <summary>
    /// Occurs when the vault is unlocked, providing the connection and metadata
    /// for the application to initialize its data layer.
    /// </summary>
    public event EventHandler<VaultUnlockedEventArgs>? VaultUnlockedRequested;

    /// <summary>
    /// Occurs when the user wants to switch to a different vault.
    /// </summary>
    public event EventHandler<VaultSwitchRequestedEventArgs>? SwitchVaultRequested;

    /// <summary>
    /// Occurs when the user requests to export the vault data.
    /// </summary>
    public event EventHandler? ExportRequested;

    /// <summary>
    /// Occurs when the user requests to import entries into the active vault.
    /// </summary>
    public event EventHandler? ImportRequested;

    public event EventHandler? RestoreBackupRequested;

    public MainWindowViewModel(
        VaultListViewModel vaultListVM,
        EntryListViewModel entryListVM,
        EntryDetailViewModel entryDetailVM,
        IEventBus eventBus,
        IBackupService backupService,
        IGlobalHotkeyService globalHotkeyService)
    {
        VaultListVM = vaultListVM ?? throw new ArgumentNullException(nameof(vaultListVM));
        EntryListVM = entryListVM ?? throw new ArgumentNullException(nameof(entryListVM));
        EntryDetailVM = entryDetailVM ?? throw new ArgumentNullException(nameof(entryDetailVM));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _backupService = backupService ?? throw new ArgumentNullException(nameof(backupService));
        _globalHotkeyService = globalHotkeyService ?? throw new ArgumentNullException(nameof(globalHotkeyService));

        // Wire up child ViewModel events
        VaultListVM.SelectionChanged += OnVaultListSelectionChanged;
        VaultListVM.VaultSelectionRequested += (_, e) =>
            SwitchVaultRequested?.Invoke(this, new VaultSwitchRequestedEventArgs(e.VaultPath));
        EntryListVM.EntrySelected += OnEntrySelected;
        EntryListVM.EntryCreated += OnEntryCreated;
        EntryDetailVM.EntryDeleted += OnEntryDeleted;
        EntryDetailVM.EntrySaved += OnEntrySaved;

        // Subscribe to application-level events
        _eventBus.Subscribe<VaultLockedEvent>(OnVaultLocked);
        _eventBus.Subscribe<VaultUnlockedEvent>(OnVaultUnlockedEvent);
        _eventBus.Subscribe<IdleTimeoutEvent>(OnIdleTimeout);

        // Subscribe to global hotkey
        _globalHotkeyService.HotkeyPressed += OnGlobalHotkeyPressed;
        LocalizationManager.Instance.LanguageChanged += (_, _) => UpdateStatusMessage();

        // Initial state
        VaultState = VaultState.Locked;
        UpdateStatusMessage();
    }

    #region Properties

    /// <summary>
    /// ViewModel for the left navigation panel.
    /// </summary>
    public VaultListViewModel VaultListVM { get; }

    /// <summary>
    /// ViewModel for the middle entry list panel.
    /// </summary>
    public EntryListViewModel EntryListVM { get; }

    /// <summary>
    /// ViewModel for the right entry detail panel.
    /// </summary>
    public EntryDetailViewModel EntryDetailVM { get; }

    /// <summary>
    /// The current state of the vault.
    /// </summary>
    [ObservableProperty]
    private VaultState _vaultState;

    /// <summary>
    /// Status message displayed in the status bar.
    /// </summary>
    [ObservableProperty]
    private string? _statusMessage;

    /// <summary>
    /// Whether the vault is currently locked.
    /// </summary>
    [ObservableProperty]
    private bool _isLocked = true;

    /// <summary>
    /// Whether the vault content is accessible (unlocked and loaded).
    /// </summary>
    [ObservableProperty]
    private bool _canAccessVault;

    /// <summary>
    /// Total number of entries in the current vault.
    /// </summary>
    [ObservableProperty]
    private int _totalEntries;

    /// <summary>
    /// Timestamp of the last successful backup.
    /// </summary>
    [ObservableProperty]
    private DateTime? _lastBackupTime;

    /// <summary>
    /// File path of the currently opened vault.
    /// </summary>
    [ObservableProperty]
    private string? _currentVaultPath;

    /// <summary>
    /// Hotkey text displayed in the main toolbar.
    /// </summary>
    [ObservableProperty]
    private string _globalSearchHotkey = "Ctrl+Shift+P";

    [ObservableProperty]
    private GridLength _sidebarWidth = new(220);

    [ObservableProperty]
    private GridLength _entryListWidth = new(320);

    #endregion

    #region Commands

    /// <summary>
    /// Locks the vault, destroying the in-memory key and clearing sensitive data.
    /// </summary>
    [RelayCommand]
    private void LockVault()
    {
        VaultState = VaultState.Locked;
        IsLocked = true;
        CanAccessVault = false;
        EntryDetailVM.ClearDetail();

        _eventBus.Publish(new VaultLockedEvent
        {
            Reason = LockReason.Manual,
            VaultFilePath = CurrentVaultPath,
        });

        UpdateStatusMessage();
    }

    /// <summary>
    /// Requests to unlock the vault. The UI layer should show the unlock dialog.
    /// </summary>
    [RelayCommand]
    private void UnlockVault()
    {
        // The actual unlock logic is handled by the UnlockViewModel.
        // This command signals the View to show the unlock dialog.
        VaultUnlockedRequested?.Invoke(this, new VaultUnlockedEventArgs(CurrentVaultPath));
    }

    /// <summary>
    /// Opens the settings dialog.
    /// </summary>
    [RelayCommand]
    private void OpenSettings()
    {
        SettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Opens the global search popup.
    /// </summary>
    [RelayCommand]
    private void OpenGlobalSearch()
    {
        if (CanAccessVault)
        {
            GlobalSearchRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Opens the setup wizard for creating a new vault.
    /// </summary>
    [RelayCommand]
    private void OpenSetupWizard()
    {
        SetupWizardRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Initiates switching to a different vault.
    /// </summary>
    [RelayCommand]
    private void SwitchVault()
    {
        SwitchVaultRequested?.Invoke(this, new VaultSwitchRequestedEventArgs(null));
    }

    /// <summary>
    /// Opens the export dialog to export vault data.
    /// </summary>
    [RelayCommand]
    private void ExportVault()
    {
        if (CanAccessVault)
        {
            ExportRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Opens the CSV import wizard for the active vault.
    /// </summary>
    [RelayCommand]
    private void ImportVault()
    {
        if (CanAccessVault)
        {
            ImportRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    [RelayCommand]
    private void RestoreBackup()
    {
        if (CanAccessVault)
            RestoreBackupRequested?.Invoke(this, EventArgs.Empty);
    }

    #endregion

    #region Vault State Transitions

    /// <summary>Rebinds all three panes to the repository for the active vault.</summary>
    public void SetRepository(IVaultRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        VaultListVM.SetRepository(repository);
        EntryListVM.SetRepository(repository);
        EntryDetailVM.SetRepository(repository);
    }

    /// <summary>
    /// Called when a vault has been successfully unlocked.
    /// Initializes the data layer and loads entries.
    /// </summary>
    /// <param name="vaultPath">The path to the vault file.</param>
    public async Task OnVaultUnlockedAsync(string vaultPath)
    {
        CurrentVaultPath = vaultPath;
        VaultState = VaultState.Unlocked;
        IsLocked = false;
        CanAccessVault = true;

        await VaultListVM.SetActiveVaultAsync(vaultPath);
        await EntryListVM.LoadEntriesAsync();

        TotalEntries = EntryListVM.Entries.Count;
        UpdateStatusMessage();
    }

    /// <summary>
    /// Updates the total entry count and refreshes the status bar.
    /// </summary>
    public void RefreshEntryCount()
    {
        TotalEntries = EntryListVM.Entries.Count;
        UpdateStatusMessage();
    }

    /// <summary>
    /// Records a backup timestamp.
    /// </summary>
    public void RecordBackup(DateTime timestamp)
    {
        LastBackupTime = timestamp;
        UpdateStatusMessage();
    }

    #endregion

    #region Event Handlers

    private async void OnVaultListSelectionChanged(object? sender, NavigationSelectionChangedEventArgs e)
    {
        try
        {
            if (e.SpecialView != null)
            {
                await EntryListVM.LoadSpecialViewAsync(e.SpecialView);
            }
            else if (e.SelectedGroup != null)
            {
                await EntryListVM.LoadEntriesAsync(e.SelectedGroup.Id);
            }
        }
        catch (Exception ex)
        {
            OnError(ex, "handling navigation selection");
        }
    }

    private async void OnEntrySelected(object? sender, VaultEntry? entry)
    {
        if (entry != null)
        {
            bool beginEditing = entry.Id == _entryIdToEditAfterSelection;
            if (beginEditing)
                _entryIdToEditAfterSelection = null;

            await EntryDetailVM.LoadEntryAsync(entry.Id);
            if (beginEditing)
                await EntryDetailVM.ToggleEditCommand.ExecuteAsync(null);
        }
        else
        {
            EntryDetailVM.ClearDetail();
        }
    }

    private void OnEntryCreated(object? sender, VaultEntry entry)
    {
        _entryIdToEditAfterSelection = entry.Id;
        RefreshEntryCount();
    }

    private void OnEntryDeleted(object? sender, string entryId)
    {
        EntryListVM.RemoveEntryFromList(entryId);
        RefreshEntryCount();
    }

    private async void OnEntrySaved(object? sender, VaultEntry entry)
    {
        EntryListVM.UpdateEntryInList(entry);
        await EntryListVM.RefreshAsync();
    }

    private void OnVaultLocked(VaultLockedEvent evt)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            VaultState = VaultState.Locked;
            IsLocked = true;
            CanAccessVault = false;
            EntryDetailVM.ClearDetail();
            UpdateStatusMessage();
        });
    }

    private void OnVaultUnlockedEvent(VaultUnlockedEvent evt)
    {
        // This event is handled by OnVaultUnlockedAsync directly
    }

    private void OnIdleTimeout(IdleTimeoutEvent evt)
    {
        // Auto-lock is triggered by the AutoLockBehavior; this is informational
    }

    private void OnGlobalHotkeyPressed()
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            if (CanAccessVault)
            {
                GlobalSearchRequested?.Invoke(this, EventArgs.Empty);
            }
        });
    }

    #endregion

    #region Private Helpers

    private void UpdateStatusMessage()
    {
        if (IsLocked)
        {
            StatusMessage = L.T("已锁定");
        }
        else
        {
            var parts = new System.Collections.Generic.List<string>();
            parts.Add(L.T("已解锁"));
            parts.Add(L.F("{0} 个条目", TotalEntries));

            if (LastBackupTime.HasValue)
            {
                var elapsed = DateTime.UtcNow - LastBackupTime.Value;
                string backupText = elapsed.TotalMinutes < 1
                    ? L.T("刚刚备份")
                    : L.F("上次备份：{0} 分钟前", (int)elapsed.TotalMinutes);
                parts.Add(backupText);
            }

            StatusMessage = string.Join(" \u00B7 ", parts); // · separator
        }
    }

    #endregion

    protected override void OnError(Exception ex, string errorContext)
    {
        base.OnError(ex, errorContext);
    }
}

public sealed class VaultSwitchRequestedEventArgs : EventArgs
{
    public VaultSwitchRequestedEventArgs(string? vaultPath) => VaultPath = vaultPath;
    public string? VaultPath { get; }
}

/// <summary>
/// Event arguments for vault unlock requests.
/// </summary>
public sealed class VaultUnlockedEventArgs : EventArgs
{
    public string? VaultPath { get; }

    public VaultUnlockedEventArgs(string? vaultPath)
    {
        VaultPath = vaultPath;
    }
}
