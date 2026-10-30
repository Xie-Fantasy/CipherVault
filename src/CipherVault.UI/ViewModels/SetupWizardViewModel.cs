using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CipherVault.Core.Interfaces;
using CipherVault.Core.Models;
using CipherVault.Core.Security;
using CipherVault.Infrastructure.Crypto;
using CipherVault.Infrastructure.Vault;
using CipherVault.UI.Helpers;
using CipherVault.UI.Localization;

namespace CipherVault.UI.ViewModels;

/// <summary>
/// ViewModel for the first-time setup wizard.
/// Guides the user through four steps:
///   1. Welcome
///   2. Create vault (name + location)
///   3. Set master password (with strength indicator)
///   4. Show recovery key + verify
/// </summary>
public partial class SetupWizardViewModel : ViewModelBase
{
    private readonly EncryptedVaultFileManager _vaultFileManager;
    private readonly RecoveryKeyGenerator _recoveryKeyGenerator;
    private readonly ICryptoService _cryptoService;
    private bool _isUsingDefaultLocation = true;

    /// <summary>
    /// Occurs when the setup wizard completes successfully.
    /// Provides the created vault file path and metadata.
    /// </summary>
    public event EventHandler<SetupCompletedEventArgs>? SetupCompleted;

    /// <summary>
    /// Occurs when the user cancels the wizard.
    /// </summary>
    public event EventHandler? Cancelled;

    /// <summary>
    /// Requests the view to show the native Windows save-location picker.
    /// </summary>
    public event EventHandler? BrowseLocationRequested;

    public SetupWizardViewModel(
        EncryptedVaultFileManager vaultFileManager,
        RecoveryKeyGenerator recoveryKeyGenerator,
        ICryptoService cryptoService)
    {
        _vaultFileManager = vaultFileManager ?? throw new ArgumentNullException(nameof(vaultFileManager));
        _recoveryKeyGenerator = recoveryKeyGenerator ?? throw new ArgumentNullException(nameof(recoveryKeyGenerator));
        _cryptoService = cryptoService ?? throw new ArgumentNullException(nameof(cryptoService));

        VaultName = L.T("个人保险库");
        VaultFilePath = BuildDefaultVaultFilePath(VaultName);

        // Pick a random word index for verification (0-based, will be shown as 1-based to user)
        VerificationWordIndex = RandomNumberGenerator.GetInt32(RecoveryKeyGenerator.MnemonicWordCount);

        // Initialize CanGoNext for the current step (Step 1 always allows proceeding)
        ValidateCurrentStep();
    }

    #region Properties

    /// <summary>
    /// Current step in the wizard (1-4).
    /// </summary>
    [ObservableProperty]
    private int _currentStep = 1;

    /// <summary>
    /// Name for the new vault (Step 2).
    /// </summary>
    [ObservableProperty]
    private string _vaultName = string.Empty;

    /// <summary>
    /// File path where the .cvault file will be created (Step 2).
    /// </summary>
    [ObservableProperty]
    private string _vaultFilePath = string.Empty;

    /// <summary>
    /// Master password input (Step 3).
    /// </summary>
    [ObservableProperty]
    private string _masterPassword = string.Empty;

    /// <summary>
    /// Confirmation of the master password (Step 3).
    /// </summary>
    [ObservableProperty]
    private string _confirmPassword = string.Empty;

    /// <summary>
    /// Password strength score (0-4, from zxcvbn).
    /// </summary>
    [ObservableProperty]
    private int _passwordStrength;

    /// <summary>
    /// Human-readable password strength label in Chinese ("弱"/"中"/"强").
    /// </summary>
    [ObservableProperty]
    private string _passwordStrengthLabel = string.Empty;

    /// <summary>
    /// The generated recovery key (Step 4).
    /// </summary>
    [ObservableProperty]
    private RecoveryKey? _recoveryKey;

    /// <summary>
    /// Numbered recovery words displayed by the wizard.
    /// </summary>
    [ObservableProperty]
    private IReadOnlyList<RecoveryWordDisplay> _recoveryKeyDisplay = Array.Empty<RecoveryWordDisplay>();

    /// <summary>
    /// Zero-based index of the word the user must verify (displayed as 1-based).
    /// </summary>
    [ObservableProperty]
    private int _verificationWordIndex;

    /// <summary>One-based recovery word number shown to the user.</summary>
    public int VerificationWordNumber => VerificationWordIndex + 1;

    /// <summary>
    /// The word the user enters to verify they wrote down the recovery key.
    /// </summary>
    [ObservableProperty]
    private string _verificationWordInput = string.Empty;

    /// <summary>
    /// Error or validation message.
    /// </summary>
    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>
    /// Indicates whether the vault creation process is running.
    /// </summary>
    [ObservableProperty]
    private bool _isCreating;

    /// <summary>
    /// Whether the user can proceed to the next step.
    /// </summary>
    [ObservableProperty]
    private bool _canGoNext;

    /// <summary>
    /// Whether the user can go back to the previous step.
    /// </summary>
    [ObservableProperty]
    private bool _canGoBack;

    /// <summary>
    /// Whether the Finish button is enabled (Step 4 validation passes).
    /// </summary>
    [ObservableProperty]
    private bool _canFinish;

    #endregion

    #region Property Changed Hooks

    partial void OnVaultNameChanged(string value)
    {
        if (_isUsingDefaultLocation)
            VaultFilePath = BuildDefaultVaultFilePath(value);

        ValidateCurrentStep();
    }

    partial void OnVaultFilePathChanged(string value)
    {
        ValidateCurrentStep();
    }

    partial void OnMasterPasswordChanged(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            PasswordStrength = 0;
            PasswordStrengthLabel = string.Empty;
        }
        else
        {
            var (score, label) = PasswordStrengthEstimator.Estimate(value);
            PasswordStrength = score;
            PasswordStrengthLabel = label;
        }

        ValidateCurrentStep();
    }

    partial void OnConfirmPasswordChanged(string value)
    {
        ValidateCurrentStep();
    }

    partial void OnVerificationWordInputChanged(string value)
    {
        ValidateCurrentStep();
    }

    partial void OnVerificationWordIndexChanged(int value)
    {
        OnPropertyChanged(nameof(VerificationWordNumber));
    }

    partial void OnCurrentStepChanged(int value)
    {
        CanGoBack = value > 1;
        ValidateCurrentStep();
    }

    #endregion

    #region Commands

    /// <summary>
    /// Advances to the next wizard step after validating the current step.
    /// </summary>
    [RelayCommand]
    private void NextStep()
    {
        if (!CanGoNext)
            return;

        if (CurrentStep == 2)
        {
            // Generate recovery key when leaving step 2 (so it's ready for step 4)
            // Actually, we generate it on entering step 4
        }

        if (CurrentStep == 3 && RecoveryKey == null)
        {
            // Generate recovery key before moving to step 4
            GenerateRecoveryKey();
        }

        if (CurrentStep < 4)
        {
            CurrentStep++;
        }
    }

    /// <summary>
    /// Returns to the previous wizard step.
    /// </summary>
    [RelayCommand]
    private void PreviousStep()
    {
        if (CurrentStep > 1)
        {
            ErrorMessage = null;
            CurrentStep--;
        }
    }

    /// <summary>
    /// Requests a native file browser dialog to choose the vault file location.
    /// </summary>
    [RelayCommand]
    private void BrowseLocation()
    {
        BrowseLocationRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Applies a path selected by the native picker and stops automatic filename updates.
    /// </summary>
    public void UseSelectedVaultFilePath(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _isUsingDefaultLocation = false;
        VaultFilePath = filePath;
    }

    /// <summary>
    /// Generates a new recovery key using the RecoveryKeyGenerator.
    /// </summary>
    [RelayCommand]
    private void GenerateRecoveryKey()
    {
        try
        {
            RecoveryKey = _recoveryKeyGenerator.Generate();

            RecoveryKeyDisplay = RecoveryKey.Words
                .Select((word, index) => new RecoveryWordDisplay(index + 1, word))
                .ToArray();

            // Pick a new random verification word index
            VerificationWordIndex = RandomNumberGenerator.GetInt32(RecoveryKey.Words.Length);
            VerificationWordInput = string.Empty;
        }
        catch (Exception ex)
        {
            OnError(ex, "generating recovery key");
            ErrorMessage = L.T("无法生成恢复密钥。");
        }
    }

    /// <summary>
    /// Completes the setup wizard: creates the vault file with the chosen master password.
    /// </summary>
    [RelayCommand]
    private async Task FinishAsync()
    {
        if (!CanFinish)
            return;

        IsCreating = true;
        ErrorMessage = null;

        try
        {
            // Ensure the directory exists
            var directory = System.IO.Path.GetDirectoryName(VaultFilePath);
            if (!string.IsNullOrEmpty(directory) && !System.IO.Directory.Exists(directory))
            {
                System.IO.Directory.CreateDirectory(directory);
            }

            if (RecoveryKey == null)
                throw new InvalidOperationException("Recovery key has not been generated.");

            byte[] passwordBytes = Encoding.UTF8.GetBytes(MasterPassword);
            try
            {
                // Create the payload and wrap its actual encryption key with the
                // recovery-derived key in one atomic file creation operation.
                var metadata = await Task.Run(() =>
                    _vaultFileManager.CreateNew(
                        VaultFilePath,
                        VaultName,
                        passwordBytes,
                        RecoveryKey.Words));

                SetupCompleted?.Invoke(this, new SetupCompletedEventArgs(VaultFilePath, metadata.Name));

                MasterPassword = string.Empty;
                ConfirmPassword = string.Empty;
                VerificationWordInput = string.Empty;
                Array.Clear(RecoveryKey.Words);
                RecoveryKey = null;
                RecoveryKeyDisplay = Array.Empty<RecoveryWordDisplay>();
            }
            finally
            {
                CryptographicOperations.ZeroMemory(passwordBytes);
            }
        }
        catch (Exception ex)
        {
            OnError(ex, "creating vault");
            ErrorMessage = L.F("创建保险库失败：{0}", ex.Message);
        }
        finally
        {
            IsCreating = false;
        }
    }

    /// <summary>
    /// Cancels the wizard and discards all input.
    /// </summary>
    [RelayCommand]
    private void Cancel()
    {
        // Clear sensitive data
        MasterPassword = string.Empty;
        ConfirmPassword = string.Empty;
        VerificationWordInput = string.Empty;
        if (RecoveryKey != null)
        {
            Array.Clear(RecoveryKey.Words);
            RecoveryKey = null;
        }

        RecoveryKeyDisplay = Array.Empty<RecoveryWordDisplay>();

        Cancelled?.Invoke(this, EventArgs.Empty);
    }

    #endregion

    #region Validation

    private static string BuildDefaultVaultFilePath(string vaultName)
    {
        string safeName = string.IsNullOrWhiteSpace(vaultName)
            ? L.T("个人保险库")
            : vaultName.Trim();

        foreach (char invalidCharacter in System.IO.Path.GetInvalidFileNameChars())
            safeName = safeName.Replace(invalidCharacter, '_');

        string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return System.IO.Path.Combine(
            documentsPath,
            "CipherVault",
            $"{safeName}.cvault");
    }

    private void ValidateCurrentStep()
    {
        ErrorMessage = null;
        CanGoNext = false;
        CanFinish = false;

        switch (CurrentStep)
        {
            case 1:
                // Welcome step: always allow next
                CanGoNext = true;
                break;

            case 2:
                // Vault name and file path required
                CanGoNext = !string.IsNullOrWhiteSpace(VaultName)
                            && !string.IsNullOrWhiteSpace(VaultFilePath);
                break;

            case 3:
                // Password requirements: 6-128 chars, confirmation must match.
                bool passwordValid = MasterPasswordPolicy.IsValid(MasterPassword)
                                     && MasterPassword == ConfirmPassword;

                if (!string.IsNullOrEmpty(MasterPassword)
                    && MasterPassword.Length < MasterPasswordPolicy.MinimumLength)
                {
                    ErrorMessage = L.F("主密码至少需要 {0} 个字符。", MasterPasswordPolicy.MinimumLength);
                }
                else if (MasterPassword.Length > MasterPasswordPolicy.MaximumLength)
                {
                    ErrorMessage = L.F("主密码最多允许 {0} 个字符。", MasterPasswordPolicy.MaximumLength);
                }
                else if (!string.IsNullOrEmpty(ConfirmPassword) && MasterPassword != ConfirmPassword)
                {
                    ErrorMessage = L.T("两次输入的密码不一致。");
                }

                CanGoNext = passwordValid;
                break;

            case 4:
                // Recovery key verification
                if (RecoveryKey != null && !string.IsNullOrWhiteSpace(VerificationWordInput))
                {
                    string expectedWord = RecoveryKey.Words[VerificationWordIndex];
                    bool matches = string.Equals(
                        VerificationWordInput.Trim(),
                        expectedWord,
                        StringComparison.OrdinalIgnoreCase);

                    if (!matches && VerificationWordInput.Length >= 2)
                    {
                        ErrorMessage = L.F("第 {0} 个恢复词不正确。", VerificationWordIndex + 1);
                    }

                    CanFinish = matches;
                }
                break;
        }
    }

    #endregion

    protected override void OnError(Exception ex, string errorContext)
    {
        base.OnError(ex, errorContext);
    }
}

/// <summary>
/// A numbered recovery word prepared for the 4-by-3 setup grid.
/// </summary>
public sealed record RecoveryWordDisplay(int Index, string Word);

/// <summary>
/// Event arguments for the setup wizard completion.
/// </summary>
public sealed class SetupCompletedEventArgs : EventArgs
{
    /// <summary>
    /// Path to the newly created .cvault file.
    /// </summary>
    public string VaultFilePath { get; }

    /// <summary>
    /// Name of the new vault.
    /// </summary>
    public string VaultName { get; }

    public SetupCompletedEventArgs(string vaultFilePath, string vaultName)
    {
        VaultFilePath = vaultFilePath;
        VaultName = vaultName;
    }
}
