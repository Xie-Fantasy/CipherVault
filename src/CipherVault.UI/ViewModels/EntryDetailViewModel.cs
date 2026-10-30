using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CipherVault.Core.Interfaces;
using CipherVault.Core.Models;
using CipherVault.Infrastructure.Crypto;
using CipherVault.UI.Localization;

namespace CipherVault.UI.ViewModels;

/// <summary>
/// ViewModel for the right panel.
/// Displays entry details, supports editing, password visibility toggle,
/// history viewing, and clipboard operations.
/// </summary>
public partial class EntryDetailViewModel : ViewModelBase
{
    private IVaultRepository _repository;
    private readonly IClipboardGuard _clipboardGuard;
    private readonly ICryptoService _cryptoService;
    private readonly VaultKeyManager _keyManager;
    private bool _passwordLoadedForEdit;
    private int _loadGeneration;

    /// <summary>
    /// Occurs when an entry has been deleted, signaling the list to remove it.
    /// </summary>
    public event EventHandler<string>? EntryDeleted;

    /// <summary>
    /// Occurs when an entry has been saved, signaling the list to update it.
    /// </summary>
    public event EventHandler<VaultEntry>? EntrySaved;

    public EntryDetailViewModel(
        IVaultRepository repository,
        IClipboardGuard clipboardGuard,
        ICryptoService cryptoService,
        VaultKeyManager keyManager)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _clipboardGuard = clipboardGuard ?? throw new ArgumentNullException(nameof(clipboardGuard));
        _cryptoService = cryptoService ?? throw new ArgumentNullException(nameof(cryptoService));
        _keyManager = keyManager ?? throw new ArgumentNullException(nameof(keyManager));
    }

    /// <summary>Rebinds this view model to the currently unlocked vault.</summary>
    public void SetRepository(IVaultRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        ClearDetail();
    }

    #region Properties

    /// <summary>
    /// The entry currently displayed in the detail panel.
    /// </summary>
    [ObservableProperty]
    private VaultEntry? _currentEntry;

    /// <summary>
    /// Whether the entry is in edit mode.
    /// </summary>
    [ObservableProperty]
    private bool _isEditing;

    /// <summary>
    /// Whether the password is currently visible in plaintext.
    /// </summary>
    [ObservableProperty]
    private bool _showPassword;

    /// <summary>
    /// The decrypted password in plaintext. Only populated when ShowPassword is true.
    /// </summary>
    [ObservableProperty]
    private string? _decryptedPassword;

    /// <summary>
    /// History snapshots of the current entry, ordered by timestamp descending.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<EntryHistory> _historyEntries = new();

    /// <summary>
    /// Tags associated with the current entry.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<Tag> _tags = new();

    /// <summary>
    /// Indicates whether data is being loaded.
    /// </summary>
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>
    /// Feedback message shown after a copy operation (e.g., "已复制 ✓").
    /// </summary>
    [ObservableProperty]
    private string? _copyFeedback;

    /// <summary>
    /// Whether the history panel is visible.
    /// </summary>
    [ObservableProperty]
    private bool _isHistoryVisible;

    /// <summary>Whether the selected entry is currently in the recycle bin.</summary>
    [ObservableProperty]
    private bool _isCurrentEntryDeleted;

    /// <summary>
    /// Error message displayed in the detail panel.
    /// </summary>
    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>
    /// Editable title for the entry (used during edit mode).
    /// </summary>
    [ObservableProperty]
    private string _editTitle = string.Empty;

    /// <summary>
    /// Editable username for the entry.
    /// </summary>
    [ObservableProperty]
    private string? _editUsername;

    /// <summary>
    /// Editable plaintext password for the entry.
    /// </summary>
    [ObservableProperty]
    private string? _editPassword;

    /// <summary>
    /// Editable URL for the entry.
    /// </summary>
    [ObservableProperty]
    private string? _editUrl;

    /// <summary>
    /// Editable notes for the entry.
    /// </summary>
    [ObservableProperty]
    private string? _editNotes;

    /// <summary>
    /// Editable credential type.
    /// </summary>
    [ObservableProperty]
    private CredentialType _editCredentialType;

    /// <summary>
    /// Editable custom fields.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<CustomField> _editCustomFields = new();

    /// <summary>Credential types available in the edit form.</summary>
    public IReadOnlyList<CredentialType> CredentialTypes { get; } = Enum.GetValues<CredentialType>();

    #endregion

    #region Commands

    /// <summary>
    /// Toggles between view and edit mode for the current entry.
    /// </summary>
    [RelayCommand]
    private async Task ToggleEditAsync()
    {
        if (CurrentEntry == null)
            return;

        if (IsEditing)
        {
            await CancelEditAsync();
        }
        else
        {
            BeginEdit();
        }
    }

    /// <summary>
    /// Saves the edited entry to the repository.
    /// </summary>
    [RelayCommand]
    private async Task SaveAsync()
    {
        if (CurrentEntry == null || !IsEditing)
            return;

        if (string.IsNullOrWhiteSpace(EditTitle))
        {
            ErrorMessage = L.T("条目名称不能为空。");
            return;
        }

        await ExecuteWithErrorHandlingAsync(async () =>
        {
            await _repository.ExecuteTransactionAsync(async () =>
            {
                // Create a history snapshot before saving.
                var snapshotJson = System.Text.Json.JsonSerializer.Serialize(CurrentEntry);
                var history = new EntryHistory
                {
                    EntryId = CurrentEntry.Id,
                    SnapshotJson = snapshotJson,
                    ChangeDescription = "entry modified",
                };
                await _repository.InsertHistoryAsync(history);

                // Apply edits.
                CurrentEntry.Title = EditTitle;
                CurrentEntry.Username = EditUsername;
                CurrentEntry.Url = EditUrl;
                CurrentEntry.Notes = EditNotes;
                CurrentEntry.CredentialType = EditCredentialType;
                CurrentEntry.ModifiedAt = DateTime.UtcNow;

                // Encrypt and store the password.
                if (_passwordLoadedForEdit && _keyManager.IsUnlocked)
                {
                    if (string.IsNullOrEmpty(EditPassword))
                    {
                        CurrentEntry.PasswordEncrypted = null;
                    }
                    else
                    {
                        byte[] plaintext = Encoding.UTF8.GetBytes(EditPassword);
                        try
                        {
                            var (ciphertext, nonce, tag) = _keyManager.UseKey(
                                key => EntryPasswordCrypto.Encrypt(
                                    _cryptoService, key, CurrentEntry.Id, plaintext));

                            // Store as Nonce(12B) + Ciphertext + Tag(16B).
                            byte[] combined = new byte[nonce.Length + ciphertext.Length + tag.Length];
                            Buffer.BlockCopy(nonce, 0, combined, 0, nonce.Length);
                            Buffer.BlockCopy(ciphertext, 0, combined, nonce.Length, ciphertext.Length);
                            Buffer.BlockCopy(tag, 0, combined, nonce.Length + ciphertext.Length, tag.Length);
                            CurrentEntry.PasswordEncrypted = combined;
                        }
                        finally
                        {
                            CryptographicOperations.ZeroMemory(plaintext);
                        }
                    }
                }

                CurrentEntry.CustomFields.Clear();
                foreach (var field in EditCustomFields)
                {
                    CurrentEntry.CustomFields.Add(new CustomField
                    {
                        Key = field.Key,
                        Value = field.Value,
                        IsSensitive = field.IsSensitive,
                    });
                }

                await _repository.UpdateEntryAsync(CurrentEntry);

                var allTags = await _repository.GetTagsAsync();
                var persistedTagIds = new List<string>();
                foreach (Tag selectedTag in Tags)
                {
                    Tag? existingTag = allTags.FirstOrDefault(tag =>
                        tag.Name.Equals(selectedTag.Name, StringComparison.OrdinalIgnoreCase));
                    if (existingTag == null)
                    {
                        await _repository.InsertTagAsync(selectedTag);
                        persistedTagIds.Add(selectedTag.Id);
                        allTags.Add(selectedTag);
                    }
                    else
                    {
                        persistedTagIds.Add(existingTag.Id);
                    }
                }
                await _repository.SetEntryTagsAsync(CurrentEntry.Id, persistedTagIds);
                CurrentEntry.TagIds = persistedTagIds;
            });

            await LoadTagsAsync();

            IsEditing = false;
            EditPassword = null;
            _passwordLoadedForEdit = false;
            ErrorMessage = null;
            OnPropertyChanged(nameof(CurrentEntry));

            EntrySaved?.Invoke(this, CurrentEntry);
        }, "saving entry");
    }

    /// <summary>
    /// Cancels editing and reverts changes.
    /// </summary>
    [RelayCommand]
    private async Task CancelEditAsync()
    {
        if (CurrentEntry == null)
            return;

        RevertEditFields();
        await LoadTagsAsync();
        IsEditing = false;
        ErrorMessage = null;
    }

    /// <summary>
    /// Toggles password visibility. When showing, decrypts the password from the encrypted field.
    /// </summary>
    [RelayCommand]
    private void TogglePasswordVisibility()
    {
        if (CurrentEntry == null)
            return;

        if (ShowPassword)
        {
            // Hide password
            ShowPassword = false;
            DecryptedPassword = null;
        }
        else
        {
            // Show password: decrypt from encrypted field
            if (CurrentEntry.PasswordEncrypted != null && _keyManager.IsUnlocked)
            {
                try
                {
                    byte[] combined = CurrentEntry.PasswordEncrypted;

                    // Extract Nonce(12B) + Ciphertext + Tag(16B)
                    if (combined.Length < 28) // 12 + 16 minimum
                    {
                        DecryptedPassword = null;
                        return;
                    }

                    byte[] nonce = new byte[12];
                    byte[] tag = new byte[16];
                    byte[] ciphertext = new byte[combined.Length - 28];

                    Buffer.BlockCopy(combined, 0, nonce, 0, 12);
                    Buffer.BlockCopy(combined, 12, ciphertext, 0, ciphertext.Length);
                    Buffer.BlockCopy(combined, combined.Length - 16, tag, 0, 16);

                    byte[] plaintext = _keyManager.UseKey(
                        key => EntryPasswordCrypto.Decrypt(
                            _cryptoService, key, CurrentEntry.Id, ciphertext, nonce, tag));
                    DecryptedPassword = Encoding.UTF8.GetString(plaintext);
                    Array.Clear(plaintext);

                    ShowPassword = true;
                }
                catch (Exception ex)
                {
                    OnError(ex, "decrypting password");
                    DecryptedPassword = null;
                    ErrorMessage = L.T("无法解密密码。");
                }
            }
            else
            {
                DecryptedPassword = null;
                ShowPassword = true; // Show empty
            }
        }
    }

    /// <summary>
    /// Copies the password to the clipboard with auto-clear protection.
    /// </summary>
    [RelayCommand]
    private async Task CopyPasswordAsync()
    {
        if (CurrentEntry == null)
            return;

        await ExecuteWithErrorHandlingAsync(async () =>
        {
            string? password = null;

            if (ShowPassword && DecryptedPassword != null)
            {
                password = DecryptedPassword;
            }
            else if (CurrentEntry.PasswordEncrypted != null && _keyManager.IsUnlocked)
            {
                byte[] combined = CurrentEntry.PasswordEncrypted;

                if (combined.Length >= 28)
                {
                    byte[] nonce = new byte[12];
                    byte[] tag = new byte[16];
                    byte[] ciphertext = new byte[combined.Length - 28];

                    Buffer.BlockCopy(combined, 0, nonce, 0, 12);
                    Buffer.BlockCopy(combined, 12, ciphertext, 0, ciphertext.Length);
                    Buffer.BlockCopy(combined, combined.Length - 16, tag, 0, 16);

                    byte[] plaintext = _keyManager.UseKey(
                        key => EntryPasswordCrypto.Decrypt(
                            _cryptoService, key, CurrentEntry.Id, ciphertext, nonce, tag));
                    password = Encoding.UTF8.GetString(plaintext);
                    Array.Clear(plaintext);
                }
            }

            if (password != null)
            {
                _clipboardGuard.CopyToClipboard(password, autoClear: true);
                CopyFeedback = L.T("已复制 ✓");
                await Task.Delay(2000);
                CopyFeedback = null;
            }
        }, "copying password");
    }

    /// <summary>
    /// Copies the username to the clipboard with auto-clear protection.
    /// </summary>
    [RelayCommand]
    private async Task CopyUsernameAsync()
    {
        if (CurrentEntry?.Username == null)
            return;

        _clipboardGuard.CopyToClipboard(CurrentEntry.Username, autoClear: true);
        CopyFeedback = L.T("已复制 ✓");
        await Task.Delay(2000);
        CopyFeedback = null;
    }

    /// <summary>Copies the URL without bypassing clipboard auto-clear protection.</summary>
    [RelayCommand]
    private async Task CopyUrlAsync()
    {
        if (string.IsNullOrWhiteSpace(CurrentEntry?.Url))
            return;

        _clipboardGuard.CopyToClipboard(CurrentEntry.Url, autoClear: true);
        CopyFeedback = L.T("已复制 ✓");
        await Task.Delay(2000);
        CopyFeedback = null;
    }

    /// <summary>Copies a custom field through the guarded clipboard path.</summary>
    [RelayCommand]
    private async Task CopyCustomFieldAsync(CustomField? field)
    {
        if (field == null || string.IsNullOrEmpty(field.Value))
            return;

        _clipboardGuard.CopyToClipboard(field.Value, autoClear: true);
        CopyFeedback = L.T("已复制 ✓");
        await Task.Delay(2000);
        CopyFeedback = null;
    }

    public void SetExternalError(string message)
    {
        ErrorMessage = message;
    }

    /// <summary>
    /// Opens the entry's URL in the default browser.
    /// </summary>
    [RelayCommand]
    private void OpenUrl()
    {
        if (string.IsNullOrWhiteSpace(CurrentEntry?.Url))
            return;

        try
        {
            var url = CurrentEntry.Url;
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                url = "https://" + url;
            }

            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            OnError(ex, "opening URL");
        }
    }

    /// <summary>
    /// Soft-deletes the current entry.
    /// </summary>
    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (CurrentEntry == null)
            return;

        await ExecuteWithErrorHandlingAsync(async () =>
        {
            await _repository.SoftDeleteEntryAsync(CurrentEntry.Id);
            var deletedId = CurrentEntry.Id;
            CurrentEntry = null;
            IsEditing = false;
            ShowPassword = false;
            DecryptedPassword = null;

            EntryDeleted?.Invoke(this, deletedId);
        }, "deleting entry");
    }

    /// <summary>Restores the selected entry from the recycle bin.</summary>
    [RelayCommand]
    private async Task RestoreDeletedAsync()
    {
        if (CurrentEntry == null || !IsCurrentEntryDeleted)
            return;

        await ExecuteWithErrorHandlingAsync(async () =>
        {
            await _repository.RestoreEntryAsync(CurrentEntry.Id);
            CurrentEntry.DeletedAt = null;
            IsCurrentEntryDeleted = false;
            EntrySaved?.Invoke(this, CurrentEntry);
        }, "restoring deleted entry");
    }

    /// <summary>Permanently removes an entry and its related history/tag links.</summary>
    [RelayCommand]
    private async Task PermanentlyDeleteAsync()
    {
        if (CurrentEntry == null || !IsCurrentEntryDeleted)
            return;

        await ExecuteWithErrorHandlingAsync(async () =>
        {
            string deletedId = CurrentEntry.Id;
            await _repository.HardDeleteEntryAsync(deletedId);
            ClearDetail();
            EntryDeleted?.Invoke(this, deletedId);
        }, "permanently deleting entry");
    }

    /// <summary>
    /// Toggles the visibility of the entry history panel.
    /// </summary>
    [RelayCommand]
    private async Task ViewHistoryAsync()
    {
        if (CurrentEntry == null)
            return;

        if (IsHistoryVisible)
        {
            IsHistoryVisible = false;
            return;
        }

        await LoadHistoryAsync();
        IsHistoryVisible = true;
    }

    /// <summary>
    /// Restores the current entry from a historical snapshot.
    /// </summary>
    /// <param name="history">The history entry to restore from.</param>
    [RelayCommand]
    private async Task RestoreFromHistoryAsync(EntryHistory history)
    {
        if (history == null || CurrentEntry == null)
            return;

        await ExecuteWithErrorHandlingAsync(async () =>
        {
            await _repository.ExecuteTransactionAsync(async () =>
            {
                // Save current state as a history entry before restoring.
                var currentSnapshot = System.Text.Json.JsonSerializer.Serialize(CurrentEntry);
                await _repository.InsertHistoryAsync(new EntryHistory
                {
                    EntryId = CurrentEntry.Id,
                    SnapshotJson = currentSnapshot,
                    ChangeDescription = L.T("恢复前的版本"),
                });

                // Deserialize the snapshot and apply it to the current entry.
                var restored = System.Text.Json.JsonSerializer.Deserialize<VaultEntry>(history.SnapshotJson);
                if (restored != null)
                {
                    CurrentEntry.Title = restored.Title;
                    CurrentEntry.Username = restored.Username;
                    CurrentEntry.PasswordEncrypted = restored.PasswordEncrypted;
                    CurrentEntry.Url = restored.Url;
                    CurrentEntry.Notes = restored.Notes;
                    CurrentEntry.TotpSecret = restored.TotpSecret;
                    CurrentEntry.CredentialType = restored.CredentialType;
                    CurrentEntry.IsFavorite = restored.IsFavorite;
                    var validGroupIds = (await _repository.GetGroupsAsync()).Select(group => group.Id).ToHashSet();
                    CurrentEntry.GroupId = restored.GroupId != null && validGroupIds.Contains(restored.GroupId)
                        ? restored.GroupId
                        : null;
                    CurrentEntry.CustomFields = restored.CustomFields
                        .Select(field => new CustomField
                        {
                            Key = field.Key,
                            Value = field.Value,
                            IsSensitive = field.IsSensitive,
                        })
                        .ToList();
                    CurrentEntry.ModifiedAt = DateTime.UtcNow;

                    await _repository.UpdateEntryAsync(CurrentEntry);
                    var validTagIds = (await _repository.GetTagsAsync()).Select(tag => tag.Id).ToHashSet();
                    await _repository.SetEntryTagsAsync(
                        CurrentEntry.Id,
                        restored.TagIds.Where(validTagIds.Contains).ToList());
                }
            });

            if (CurrentEntry != null)
            {
                await LoadEntryAsync(CurrentEntry.Id);
                await LoadHistoryAsync();
            }
        }, "restoring from history");
    }

    /// <summary>
    /// Adds a new custom field row to the editable fields.
    /// </summary>
    [RelayCommand]
    private void AddCustomField()
    {
        EditCustomFields.Add(new CustomField
        {
            Key = string.Empty,
            Value = string.Empty,
            IsSensitive = false,
        });
    }

    /// <summary>
    /// Removes a custom field from the editable fields.
    /// </summary>
    /// <param name="field">The field to remove.</param>
    [RelayCommand]
    private void RemoveCustomField(CustomField field)
    {
        if (field != null)
        {
            EditCustomFields.Remove(field);
        }
    }

    [RelayCommand]
    private void AddTag(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) ||
            Tags.Any(tag => tag.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase)))
            return;
        Tags.Add(new Tag { Name = name.Trim() });
    }

    [RelayCommand]
    private void RemoveTag(Tag? tag)
    {
        if (tag != null)
            Tags.Remove(tag);
    }

    /// <summary>
    /// Toggles the favorite status of the current entry.
    /// </summary>
    [RelayCommand]
    private async Task ToggleFavoriteAsync()
    {
        if (CurrentEntry == null)
            return;

        await ExecuteWithErrorHandlingAsync(async () =>
        {
            CurrentEntry.IsFavorite = !CurrentEntry.IsFavorite;
            CurrentEntry.ModifiedAt = DateTime.UtcNow;
            await _repository.UpdateEntryAsync(CurrentEntry);

            EntrySaved?.Invoke(this, CurrentEntry);
        }, "toggling favorite");
    }

    #endregion

    #region Methods

    /// <summary>
    /// Loads an entry by its ID and populates the detail panel.
    /// </summary>
    /// <param name="entryId">The ID of the entry to load.</param>
    public async Task LoadEntryAsync(string entryId)
    {
        if (string.IsNullOrEmpty(entryId))
            return;

        int generation = Interlocked.Increment(ref _loadGeneration);
        IsLoading = true;
        try
        {
            var entry = await _repository.GetEntryByIdAsync(entryId);
            if (generation != Volatile.Read(ref _loadGeneration))
                return;

            if (entry == null)
            {
                CurrentEntry = null;
                return;
            }

            var entryTags = await _repository.GetTagsForEntryAsync(entry.Id);
            if (generation != Volatile.Read(ref _loadGeneration))
                return;

            CurrentEntry = entry;
            IsCurrentEntryDeleted = entry.DeletedAt.HasValue;
            IsEditing = false;
            ShowPassword = false;
            DecryptedPassword = null;
            IsHistoryVisible = false;
            CopyFeedback = null;
            ErrorMessage = null;

            Tags.Clear();
            foreach (var tag in entryTags)
                Tags.Add(tag);

            // Update last accessed time
            entry.LastAccessedAt = DateTime.UtcNow;
            await _repository.MarkEntryAccessedAsync(entry.Id, entry.LastAccessedAt.Value);
        }
        catch (Exception ex)
        {
            OnError(ex, "loading entry");
        }
        finally
        {
            if (generation == Volatile.Read(ref _loadGeneration))
                IsLoading = false;
        }
    }

    /// <summary>
    /// Loads the history for the current entry.
    /// </summary>
    public async Task LoadHistoryAsync()
    {
        if (CurrentEntry == null)
            return;

        await ExecuteWithErrorHandlingAsync(async () =>
        {
            var history = await _repository.GetEntryHistoryAsync(CurrentEntry.Id);
            HistoryEntries.Clear();
            foreach (var item in history)
            {
                HistoryEntries.Add(item);
            }
        }, "loading history");
    }

    /// <summary>
    /// Clears the detail panel (e.g., when the vault is locked).
    /// </summary>
    public void ClearDetail()
    {
        Interlocked.Increment(ref _loadGeneration);
        CurrentEntry = null;
        IsEditing = false;
        ShowPassword = false;
        DecryptedPassword = null;
        EditPassword = null;
        _passwordLoadedForEdit = false;
        EditCustomFields.Clear();
        HistoryEntries.Clear();
        Tags.Clear();
        IsHistoryVisible = false;
        IsCurrentEntryDeleted = false;
        CopyFeedback = null;
        ErrorMessage = null;
    }

    #endregion

    #region Private Helpers

    private void BeginEdit()
    {
        if (CurrentEntry == null)
            return;

        _passwordLoadedForEdit = false;
        EditTitle = CurrentEntry.Title;
        EditUsername = CurrentEntry.Username;
        EditUrl = CurrentEntry.Url;
        EditNotes = CurrentEntry.Notes;
        EditCredentialType = CurrentEntry.CredentialType;

        // Decrypt password for editing
        if (CurrentEntry.PasswordEncrypted != null && _keyManager.IsUnlocked)
        {
            try
            {
                byte[] combined = CurrentEntry.PasswordEncrypted;

                if (combined.Length >= 28)
                {
                    byte[] nonce = new byte[12];
                    byte[] tag = new byte[16];
                    byte[] ciphertext = new byte[combined.Length - 28];

                    Buffer.BlockCopy(combined, 0, nonce, 0, 12);
                    Buffer.BlockCopy(combined, 12, ciphertext, 0, ciphertext.Length);
                    Buffer.BlockCopy(combined, combined.Length - 16, tag, 0, 16);

                    byte[] plaintext = _keyManager.UseKey(
                        key => EntryPasswordCrypto.Decrypt(
                            _cryptoService, key, CurrentEntry.Id, ciphertext, nonce, tag));
                    EditPassword = Encoding.UTF8.GetString(plaintext);
                    CryptographicOperations.ZeroMemory(plaintext);
                    _passwordLoadedForEdit = true;
                }
            }
            catch
            {
                EditPassword = null;
                _passwordLoadedForEdit = false;
            }
        }
        else
        {
            EditPassword = null;
            _passwordLoadedForEdit = CurrentEntry.PasswordEncrypted == null;
        }

        // Copy custom fields for editing
        EditCustomFields.Clear();
        foreach (var field in CurrentEntry.CustomFields)
        {
            EditCustomFields.Add(new CustomField
            {
                Key = field.Key,
                Value = field.Value,
                IsSensitive = field.IsSensitive,
            });
        }

        IsEditing = true;
    }

    private void RevertEditFields()
    {
        if (CurrentEntry == null)
            return;

        EditTitle = CurrentEntry.Title;
        EditUsername = CurrentEntry.Username;
        EditPassword = null;
        _passwordLoadedForEdit = false;
        EditUrl = CurrentEntry.Url;
        EditNotes = CurrentEntry.Notes;
        EditCredentialType = CurrentEntry.CredentialType;
        EditCustomFields.Clear();
    }

    private async Task LoadTagsAsync()
    {
        if (CurrentEntry == null)
            return;

        Tags.Clear();
        var entryTags = await _repository.GetTagsForEntryAsync(CurrentEntry.Id);
        foreach (var tag in entryTags)
        {
            Tags.Add(tag);
        }
    }

    #endregion

    protected override void OnError(Exception ex, string errorContext)
    {
        base.OnError(ex, errorContext);
        ErrorMessage = L.F("操作失败：{0}", errorContext);
    }
}
