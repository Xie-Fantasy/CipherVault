using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CipherVault.Core.Interfaces;
using CipherVault.Core.Models;
using CipherVault.Infrastructure.Crypto;
using CipherVault.UI.Localization;

namespace CipherVault.UI.ViewModels;

/// <summary>
/// Copy mode for the global search popup.
/// </summary>
public enum CopyMode
{
    /// <summary>Copy the entry's username.</summary>
    Username,

    /// <summary>Copy the entry's password (default).</summary>
    Password,
}

/// <summary>
/// ViewModel for the global search popup window.
/// Provides real-time search across all entries with explicit copy actions.
/// Triggered by global hotkey (Ctrl+Shift+P).
/// </summary>
public partial class GlobalSearchViewModel : ViewModelBase
{
    private readonly IVaultRepository _repository;
    private readonly IClipboardGuard _clipboardGuard;
    private readonly ICryptoService _cryptoService;
    private readonly VaultKeyManager _keyManager;
    private CancellationTokenSource? _searchCts;

    /// <summary>
    /// Occurs when a copy operation is completed, providing the entry and the field type that was copied.
    /// </summary>
    public event EventHandler<CopyCompletedEventArgs>? CopyRequested;

    /// <summary>
    /// Occurs when the popup should be closed.
    /// </summary>
    public event EventHandler? Closed;

    public GlobalSearchViewModel(
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

    #region Properties

    /// <summary>
    /// The search query. Changing this triggers a debounced real-time search.
    /// </summary>
    [ObservableProperty]
    private string _searchQuery = string.Empty;

    /// <summary>
    /// The search results matching the current query.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<VaultEntry> _results = new();

    /// <summary>
    /// The currently selected entry in the results list.
    /// </summary>
    [ObservableProperty]
    private VaultEntry? _selectedEntry;

    /// <summary>
    /// The current copy mode (Username or Password). Tab toggles between modes.
    /// </summary>
    [ObservableProperty]
    private CopyMode _copyMode = CopyMode.Password;

    /// <summary>
    /// Whether a search is currently in progress.
    /// </summary>
    [ObservableProperty]
    private bool _isSearching;

    /// <summary>
    /// Feedback message (e.g., "copied" confirmation).
    /// </summary>
    [ObservableProperty]
    private string? _feedbackMessage;

    #endregion

    #region Property Changed Hooks

    partial void OnSearchQueryChanged(string value)
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        _ = DebounceSearchAsync(value, token);
    }

    private async Task DebounceSearchAsync(string value, CancellationToken token)
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
            // Expected when the query changes or the popup closes.
        }
    }

    #endregion

    #region Commands

    /// <summary>
    /// Executes the copy operation for the selected entry.
    /// Bound to the Enter key.
    /// </summary>
    [RelayCommand]
    private async Task ExecuteCopyAsync()
    {
        if (SelectedEntry == null)
            return;

        try
        {
            if (CopyMode == CopyMode.Password)
            {
                string? password = DecryptPassword(SelectedEntry);
                if (password != null)
                {
                    _clipboardGuard.CopyToClipboard(password, autoClear: true);
                    FeedbackMessage = L.T("密码已复制");
                    CopyRequested?.Invoke(this, new CopyCompletedEventArgs(SelectedEntry, "password"));
                }
                else
                {
                    FeedbackMessage = L.T("此条目没有密码");
                    return;
                }
            }
            else
            {
                if (!string.IsNullOrEmpty(SelectedEntry.Username))
                {
                    _clipboardGuard.CopyToClipboard(SelectedEntry.Username, autoClear: true);
                    FeedbackMessage = L.T("账号已复制");
                    CopyRequested?.Invoke(this, new CopyCompletedEventArgs(SelectedEntry, "username"));
                }
                else
                {
                    FeedbackMessage = L.T("此条目没有账号");
                    return;
                }
            }

            // Close the popup after copying
            await Task.Delay(300);
            Close();
        }
        catch (Exception ex)
        {
            OnError(ex, "copying from global search");
        }
    }

    /// <summary>
    /// Switches between Username and Password copy modes.
    /// Bound to the Tab key.
    /// </summary>
    [RelayCommand]
    private void SwitchCopyMode()
    {
        CopyMode = CopyMode == CopyMode.Password ? CopyMode.Username : CopyMode.Password;
    }

    /// <summary>
    /// Closes the global search popup and clears the search state.
    /// Bound to Escape key or loss of focus.
    /// </summary>
    [RelayCommand]
    private void Close()
    {
        SearchQuery = string.Empty;
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = null;
        Results.Clear();
        SelectedEntry = null;
        CopyMode = CopyMode.Password;
        FeedbackMessage = null;

        Closed?.Invoke(this, EventArgs.Empty);
    }

    #endregion

    #region Methods

    /// <summary>
    /// Initializes the search popup for display.
    /// Clears previous state and ensures the search field is ready.
    /// </summary>
    public void Initialize()
    {
        SearchQuery = string.Empty;
        Results.Clear();
        SelectedEntry = null;
        CopyMode = CopyMode.Password;
        FeedbackMessage = null;
    }

    /// <summary>
    /// Loads all entries as initial results when the popup opens (before any search query).
    /// </summary>
    public async Task LoadInitialResultsAsync()
    {
        try
        {
            var entries = await _repository.GetRecentEntriesAsync(20);
            Results.Clear();
            foreach (var entry in entries)
            {
                Results.Add(entry);
            }

            if (Results.Count > 0 && SelectedEntry == null)
            {
                SelectedEntry = Results[0];
            }
        }
        catch (Exception ex)
        {
            OnError(ex, "loading initial search results");
        }
    }

    #endregion

    #region Private Helpers

    private async Task PerformSearchAsync(string query, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            Results.Clear();
            await LoadInitialResultsAsync();
            return;
        }

        IsSearching = true;
        try
        {
            var searchResults = await _repository.SearchEntriesAsync(query);

            if (token.IsCancellationRequested)
                return;

            Results.Clear();
            foreach (var entry in searchResults.Take(50)) // Limit results for performance
            {
                Results.Add(entry);
            }

            SelectedEntry = Results.Count > 0 ? Results[0] : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            OnError(ex, "searching entries");
        }
        finally
        {
            IsSearching = false;
        }
    }

    private string? DecryptPassword(VaultEntry entry)
    {
        if (entry.PasswordEncrypted == null || !_keyManager.IsUnlocked)
            return null;

        try
        {
            byte[] combined = entry.PasswordEncrypted;

            if (combined.Length < 28) // 12 (nonce) + 16 (tag) minimum
                return null;

            byte[] nonce = new byte[12];
            byte[] tag = new byte[16];
            byte[] ciphertext = new byte[combined.Length - 28];

            Buffer.BlockCopy(combined, 0, nonce, 0, 12);
            Buffer.BlockCopy(combined, 12, ciphertext, 0, ciphertext.Length);
            Buffer.BlockCopy(combined, combined.Length - 16, tag, 0, 16);

            byte[] plaintext = _keyManager.UseKey(
                key => EntryPasswordCrypto.Decrypt(
                    _cryptoService, key, entry.Id, ciphertext, nonce, tag));
            string result = Encoding.UTF8.GetString(plaintext);
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(plaintext);

            return result;
        }
        catch
        {
            return null;
        }
    }

    #endregion

    protected override void OnError(Exception ex, string errorContext)
    {
        base.OnError(ex, errorContext);
    }
}

/// <summary>
/// Event arguments for a completed copy operation from the global search.
/// </summary>
public sealed class CopyCompletedEventArgs : EventArgs
{
    /// <summary>
    /// The entry that was copied from.
    /// </summary>
    public VaultEntry Entry { get; }

    /// <summary>
    /// The field type that was copied ("password" or "username").
    /// </summary>
    public string FieldType { get; }

    public CopyCompletedEventArgs(VaultEntry entry, string fieldType)
    {
        Entry = entry;
        FieldType = fieldType;
    }
}
