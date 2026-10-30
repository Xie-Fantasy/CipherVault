using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CipherVault.Core.Exceptions;
using CipherVault.Core.Interfaces;
using CipherVault.Core.Models;
using CipherVault.Infrastructure.Crypto;
using CipherVault.Infrastructure.Security;
using CipherVault.Infrastructure.Vault;
using CipherVault.UI.Localization;
using Microsoft.Data.Sqlite;

namespace CipherVault.UI.ViewModels;

/// <summary>
/// ViewModel for the unlock dialog.
/// Handles master password input, brute-force delay with countdown,
/// and vault decryption.
/// </summary>
public partial class UnlockViewModel : ViewModelBase
{
    private readonly EncryptedVaultFileManager _vaultFileManager;
    private readonly BruteForceGuard _bruteForceGuard;
    private readonly VaultKeyManager _keyManager;
    private readonly IWindowsHelloService? _windowsHelloService;
    private CancellationTokenSource? _countdownCts;

    /// <summary>
    /// Occurs when the vault is successfully unlocked.
    /// Provides the open in-memory SQLite connection and vault metadata.
    /// </summary>
    public event EventHandler<VaultUnlockCompletedEventArgs>? Unlocked;

    /// <summary>
    /// Occurs when the user clicks "Forgot Password", requesting the recovery key flow.
    /// </summary>
    public event EventHandler? ForgotPasswordRequested;

    /// <summary>
    /// Occurs when the user cancels the unlock dialog.
    /// </summary>
    public event EventHandler? Cancelled;

    public UnlockViewModel(
        EncryptedVaultFileManager vaultFileManager,
        BruteForceGuard bruteForceGuard,
        VaultKeyManager keyManager,
        IWindowsHelloService? windowsHelloService = null)
    {
        _vaultFileManager = vaultFileManager ?? throw new ArgumentNullException(nameof(vaultFileManager));
        _bruteForceGuard = bruteForceGuard ?? throw new ArgumentNullException(nameof(bruteForceGuard));
        _keyManager = keyManager ?? throw new ArgumentNullException(nameof(keyManager));
        _windowsHelloService = windowsHelloService;
    }

    #region Properties

    /// <summary>
    /// The master password entered by the user.
    /// </summary>
    [ObservableProperty]
    private string _masterPassword = string.Empty;

    /// <summary>
    /// Error message displayed when unlock fails.
    /// </summary>
    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>
    /// Indicates whether an unlock attempt is in progress.
    /// </summary>
    [ObservableProperty]
    private bool _isUnlocking;

    /// <summary>
    /// Indicates whether the vault is currently locked out due to too many failed attempts.
    /// </summary>
    [ObservableProperty]
    private bool _isLockedOut;

    /// <summary>
    /// Remaining seconds in the lockout countdown.
    /// </summary>
    [ObservableProperty]
    private int _lockoutCountdown;

    /// <summary>
    /// Display name of the vault file being unlocked.
    /// </summary>
    [ObservableProperty]
    private string? _vaultFileName;

    /// <summary>
    /// Number of failed unlock attempts so far.
    /// </summary>
    [ObservableProperty]
    private int _failedAttempts;

    /// <summary>
    /// Whether Windows Hello biometric authentication is available on this device.
    /// </summary>
    [ObservableProperty]
    private bool _windowsHelloAvailable;

    /// <summary>
    /// Whether a Windows Hello-wrapped key exists for the current vault.
    /// </summary>
    [ObservableProperty]
    private bool _hasStoredHelloKey;

    /// <summary>
    /// The file path of the vault to unlock.
    /// </summary>
    private string? _vaultFilePath;

    #endregion

    #region Commands

    /// <summary>
    /// Attempts to unlock the vault using the entered master password.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanUnlock))]
    private async Task UnlockAsync()
    {
        if (string.IsNullOrEmpty(_vaultFilePath) || _bruteForceGuard.IsLockedOut)
            return;

        IsUnlocking = true;
        ErrorMessage = null;

        try
        {
            // Check for brute-force delay
            int delayMs = _bruteForceGuard.GetDelayMilliseconds();
            if (delayMs > 0 && !_bruteForceGuard.IsLockedOut)
            {
                await Task.Delay(delayMs);
            }

            // Attempt to decrypt the vault
            byte[] passwordBytes = Encoding.UTF8.GetBytes(MasterPassword);
            try
            {
                var (connection, metadata, masterKey) =
                    _vaultFileManager.OpenAndDecryptWithKey(_vaultFilePath, passwordBytes);

                // Success: reset brute-force guard and persist
                _bruteForceGuard.RecordSuccess();
                PersistBruteForceState();

                // Store the key via Windows Hello for future biometric unlock (if available)
                if (_windowsHelloService != null)
                {
                    byte[]? helloKeyCopy = null;
                    try
                    {
                        helloKeyCopy = new byte[masterKey.Length];
                        Buffer.BlockCopy(masterKey, 0, helloKeyCopy, 0, masterKey.Length);
                        await _windowsHelloService.StoreKeyAsync(_vaultFilePath, helloKeyCopy);
                    }
                    catch (Exception)
                    {
                        // Non-critical: Windows Hello key storage failure shouldn't block unlock
                    }
                    finally
                    {
                        if (helloKeyCopy is { Length: > 0 })
                            CryptographicOperations.ZeroMemory(helloKeyCopy);
                    }
                }

                // Store the key in the key manager
                if (_keyManager.IsUnlocked)
                {
                    _keyManager.DestroyKey();
                }
                _keyManager.StoreKey(masterKey);

                // Notify listeners
                Unlocked?.Invoke(this, new VaultUnlockCompletedEventArgs(connection, metadata, _vaultFilePath));
            }
            catch (InvalidMasterPasswordException)
            {
                // Wrong password
                _bruteForceGuard.RecordFailure();
                PersistBruteForceState();

                FailedAttempts = _bruteForceGuard.FailedAttempts;

                if (_bruteForceGuard.FailedAttempts >= 20)
                {
                    ErrorMessage = L.T("失败次数过多，请使用恢复密钥。");
                }
                else if (_bruteForceGuard.FailedAttempts >= 11)
                {
                    await StartLockoutCountdownAsync();
                    ErrorMessage = L.T("失败次数过多，已锁定 5 分钟。");
                }
                else
                {
                    int remaining = 20 - _bruteForceGuard.FailedAttempts;
                    ErrorMessage = L.F("主密码不正确，还可尝试 {0} 次。", remaining);
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(passwordBytes);
                MasterPassword = string.Empty;
            }
        }
        catch (VaultCorruptedException ex)
        {
            OnError(ex, "unlocking vault");
            ErrorMessage = GetFriendlyVaultError(ex);
        }
        catch (Exception ex)
        {
            OnError(ex, "unlocking vault");
            ErrorMessage = GetFriendlyVaultError(ex);
        }
        finally
        {
            IsUnlocking = false;
            UnlockCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>
    /// Opens the recovery key input flow.
    /// </summary>
    [RelayCommand]
    private void ForgotPassword()
    {
        ForgotPasswordRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Attempts to unlock the vault using Windows Hello biometric authentication.
    /// Uses the non-exportable Windows Hello private key to unwrap the vault key,
    /// then decrypts the vault file.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanWindowsHelloUnlock))]
    private async Task WindowsHelloUnlockAsync()
    {
        if (_windowsHelloService == null || string.IsNullOrEmpty(_vaultFilePath))
            return;

        IsUnlocking = true;
        ErrorMessage = null;

        try
        {
            // Accessing the non-exportable private key invokes Windows Hello.
            byte[]? masterKey = await _windowsHelloService.RetrieveKeyAsync(_vaultFilePath);
            if (masterKey == null)
            {
                ErrorMessage = L.T("未找到已保存的密钥，请先使用主密码解锁。");
                HasStoredHelloKey = false;
                return;
            }

            try
            {
                var (connection, metadata) = _vaultFileManager.OpenWithMasterKey(_vaultFilePath, masterKey);

                // Store the key in the key manager
                if (_keyManager.IsUnlocked)
                {
                    _keyManager.DestroyKey();
                }

                // Make a copy for the key manager (StoreKey zeros the input)
                byte[] keyCopy = new byte[masterKey.Length];
                Buffer.BlockCopy(masterKey, 0, keyCopy, 0, masterKey.Length);
                _keyManager.StoreKey(keyCopy);

                // Reset brute-force guard on successful unlock
                _bruteForceGuard.RecordSuccess();
                PersistBruteForceState();

                // Notify listeners
                Unlocked?.Invoke(this, new VaultUnlockCompletedEventArgs(connection, metadata, _vaultFilePath));
            }
            catch (InvalidMasterPasswordException)
            {
                // The stored key is invalid (master password was changed)
                _windowsHelloService.RemoveStoredKey(_vaultFilePath);
                HasStoredHelloKey = false;
                ErrorMessage = L.T("已保存的密钥失效，主密码可能已更改，请使用主密码解锁。");
            }
            finally
            {
                CryptographicOperations.ZeroMemory(masterKey);
            }
        }
        catch (VaultCorruptedException ex)
        {
            OnError(ex, "windows hello unlock");
            ErrorMessage = GetFriendlyVaultError(ex, isWindowsHello: true);
        }
        catch (Exception ex)
        {
            OnError(ex, "windows hello unlock");
            ErrorMessage = GetFriendlyVaultError(ex, isWindowsHello: true);
        }
        finally
        {
            IsUnlocking = false;
            WindowsHelloUnlockCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>
    /// Cancels the unlock dialog.
    /// </summary>
    [RelayCommand]
    private void Cancel()
    {
        _countdownCts?.Cancel();
        MasterPassword = string.Empty;
        Cancelled?.Invoke(this, EventArgs.Empty);
    }

    #endregion

    #region Methods

    /// <summary>
    /// Sets the vault file path and loads its header to initialize the brute-force guard.
    /// </summary>
    /// <param name="path">The path to the .cvault file.</param>
    public void SetVaultFilePath(string path)
    {
        _vaultFilePath = path ?? throw new ArgumentNullException(nameof(path));
        VaultFileName = System.IO.Path.GetFileNameWithoutExtension(path);

        try
        {
            var header = _vaultFileManager.ReadHeader(path);
            _bruteForceGuard.LoadState(header.FailedAttempts, header.LockoutUntil);
            FailedAttempts = _bruteForceGuard.FailedAttempts;

            if (_bruteForceGuard.IsLockedOut)
            {
                _ = StartLockoutCountdownAsync();
            }
        }
        catch (Exception ex)
        {
            OnError(ex, "reading vault header");
            ErrorMessage = L.T("无法读取保险库文件。");
        }

        UnlockCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Attempts to unlock the vault using a recovery key (mnemonic words).
    /// </summary>
    /// <param name="mnemonicWords">The recovery key words entered by the user.</param>
    public async Task<bool> AttemptRecoveryUnlockAsync(string[] mnemonicWords, byte[] newMasterPassword)
    {
        if (string.IsNullOrEmpty(_vaultFilePath))
            return false;
        ArgumentNullException.ThrowIfNull(newMasterPassword);

        IsUnlocking = true;
        ErrorMessage = null;

        SqliteConnection? connection = null;
        byte[]? vaultKey = null;
        byte[]? newVaultKey = null;
        bool connectionTransferred = false;
        bool keyStored = false;
        try
        {
            var recoveryResult = await Task.Run(() =>
                _vaultFileManager.OpenWithRecoveryKey(_vaultFilePath, mnemonicWords));
            connection = recoveryResult.connection;
            var metadata = recoveryResult.metadata;
            vaultKey = recoveryResult.vaultKey;

            // Recovery is not a normal unlock: rotate the payload key, every
            // per-entry password ciphertext, salt, and recovery wrapping block.
            newVaultKey = await Task.Run(() => _vaultFileManager.ResetMasterPassword(
                _vaultFilePath,
                connection,
                metadata,
                vaultKey,
                newMasterPassword,
                mnemonicWords));

            // Reset brute-force on successful recovery
            _bruteForceGuard.RecordSuccess();
            PersistBruteForceState();

            if (_keyManager.IsUnlocked)
            {
                _keyManager.DestroyKey();
            }

            // Store the actual key unwrapped from the recovery block. This is the same
            // key used by the payload and per-entry password encryption in format v1.
            _keyManager.StoreKey(newVaultKey);
            keyStored = true;
            _windowsHelloService?.RemoveStoredKey(_vaultFilePath);

            Unlocked?.Invoke(this, new VaultUnlockCompletedEventArgs(connection, metadata, _vaultFilePath));
            connectionTransferred = true;
            return true;
        }
        catch (InvalidRecoveryKeyException)
        {
            ErrorMessage = L.T("恢复密钥与该保险库不匹配。");
            return false;
        }
        catch (VaultCorruptedException ex)
        {
            OnError(ex, "recovery unlock");
            ErrorMessage = GetFriendlyVaultError(ex);
            return false;
        }
        catch (Exception ex)
        {
            OnError(ex, "recovery unlock");
            ErrorMessage = GetFriendlyVaultError(ex);
            return false;
        }
        finally
        {
            if (vaultKey is { Length: > 0 })
                CryptographicOperations.ZeroMemory(vaultKey);
            if (newVaultKey is { Length: > 0 })
                CryptographicOperations.ZeroMemory(newVaultKey);
            CryptographicOperations.ZeroMemory(newMasterPassword);
            if (!connectionTransferred && keyStored && _keyManager.IsUnlocked)
                _keyManager.DestroyKey();
            if (!connectionTransferred)
                connection?.Dispose();
            IsUnlocking = false;
        }
    }

    #endregion

    #region Private Helpers

    private bool CanUnlock()
    {
        return !string.IsNullOrEmpty(_vaultFilePath)
               && !string.IsNullOrEmpty(MasterPassword)
               && !_bruteForceGuard.IsLockedOut
               && !IsUnlocking;
    }

    private bool CanWindowsHelloUnlock()
    {
        return _windowsHelloService != null
               && WindowsHelloAvailable
               && HasStoredHelloKey
               && !string.IsNullOrEmpty(_vaultFilePath)
               && !_bruteForceGuard.IsLockedOut
               && !IsUnlocking;
    }

    /// <summary>
    /// Checks whether Windows Hello is available and a stored key exists for the current vault.
    /// Should be called after setting the vault file path.
    /// </summary>
    public async Task CheckWindowsHelloAvailabilityAsync()
    {
        if (_windowsHelloService == null || string.IsNullOrEmpty(_vaultFilePath))
        {
            WindowsHelloAvailable = false;
            HasStoredHelloKey = false;
            return;
        }

        try
        {
            WindowsHelloAvailable = await _windowsHelloService.IsAvailableAsync();
            HasStoredHelloKey = _windowsHelloService.HasStoredKey(_vaultFilePath);
        }
        catch (Exception ex)
        {
            OnError(ex, "checking windows hello availability");
            WindowsHelloAvailable = false;
            HasStoredHelloKey = false;
        }

        WindowsHelloUnlockCommand.NotifyCanExecuteChanged();
    }

    partial void OnMasterPasswordChanged(string value)
    {
        UnlockCommand.NotifyCanExecuteChanged();
    }

    private void PersistBruteForceState()
    {
        if (string.IsNullOrEmpty(_vaultFilePath))
            return;

        try
        {
            var (attempts, lockoutUntil) = _bruteForceGuard.GetState();
            _vaultFileManager.UpdateFailedAttempts(_vaultFilePath, attempts, lockoutUntil);
        }
        catch (Exception ex)
        {
            OnError(ex, "persisting brute force state");
        }
    }

    private async Task StartLockoutCountdownAsync()
    {
        _countdownCts?.Cancel();
        _countdownCts = new CancellationTokenSource();
        var token = _countdownCts.Token;
        IsLockedOut = true;

        try
        {
            while (!token.IsCancellationRequested && _bruteForceGuard.IsLockedOut)
            {
                var remaining = _bruteForceGuard.LockoutRemaining;
                LockoutCountdown = remaining.HasValue ? (int)remaining.Value.TotalSeconds : 0;

                if (LockoutCountdown <= 0)
                    break;

                await Task.Delay(1000, token);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        IsLockedOut = false;
        LockoutCountdown = 0;
        UnlockCommand.NotifyCanExecuteChanged();
    }

    #endregion

    private static string GetFriendlyVaultError(Exception exception, bool isWindowsHello = false)
    {
        return exception switch
        {
            FileNotFoundException =>
                L.T("找不到保险库文件。请确认文件仍在原位置。"),
            UnauthorizedAccessException =>
                L.T("无法访问保险库文件。请确认当前账户拥有该文件的读写权限。"),
            SqliteException =>
                L.T("保险库数据暂时无法加载。文件没有被修改，请重试；如果问题仍然存在，请从最近备份恢复。"),
            InvalidDataException or VaultCorruptedException =>
                L.T("保险库完整性校验失败。请勿覆盖该文件，可以尝试从最近备份恢复。"),
            IOException =>
                L.T("无法读取保险库文件。请确认文件未被其他程序占用，然后重试。"),
            _ when isWindowsHello =>
                L.T("Windows Hello 验证未完成。请重试或使用主密码解锁。"),
            _ =>
                L.T("解锁时发生内部错误。保险库文件没有被修改，请重启程序后重试。"),
        };
    }

    protected override void OnError(Exception ex, string errorContext)
    {
        base.OnError(ex, errorContext);
    }
}

/// <summary>
/// Event arguments for a successful vault unlock.
/// </summary>
public sealed class VaultUnlockCompletedEventArgs : EventArgs
{
    /// <summary>
    /// Open SQLite connection to the decrypted database.
    /// </summary>
    public SqliteConnection Connection { get; }

    /// <summary>
    /// Vault file metadata.
    /// </summary>
    public VaultFile Metadata { get; }

    /// <summary>
    /// Path to the .cvault file.
    /// </summary>
    public string VaultFilePath { get; }

    public VaultUnlockCompletedEventArgs(SqliteConnection connection, VaultFile metadata, string vaultFilePath)
    {
        Connection = connection;
        Metadata = metadata;
        VaultFilePath = vaultFilePath;
    }
}
