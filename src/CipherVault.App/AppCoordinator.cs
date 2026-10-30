using System;
using System.ComponentModel;
using System.Data;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using CipherVault.App.Config;
using CipherVault.Core.Config;
using CipherVault.Core.Enums;
using CipherVault.Core.Events;
using CipherVault.Core.Interfaces;
using CipherVault.Core.Models;
using CipherVault.Infrastructure.Backup;
using CipherVault.Infrastructure.Crypto;
using CipherVault.Infrastructure.Export;
using CipherVault.Infrastructure.Import;
using CipherVault.Infrastructure.Security;
using CipherVault.Infrastructure.Vault;
using CipherVault.UI.Behaviors;
using CipherVault.UI.Localization;
using CipherVault.UI.ViewModels;
using CipherVault.UI.Views;
using CipherVault.UI.Views.Dialogs;
using CipherVault.UI.Views.Popups;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using Serilog;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace CipherVault.App;

/// <summary>
/// Main application orchestrator that manages the application lifecycle.
/// Created in App.xaml.cs after DI is built, manages MainWindow, dialogs, and vault state transitions.
/// </summary>
public sealed class AppCoordinator : IDisposable
{
    private readonly IServiceProvider _services;
    private readonly IConfigService _configService;
    private readonly ICryptoService _cryptoService;
    private readonly ISecureMemoryManager _secureMemoryManager;
    private readonly IAntiCaptureService _antiCaptureService;
    private readonly IGlobalHotkeyService _globalHotkeyService;
    private readonly IEventBus _eventBus;
    private readonly IClipboardGuard _clipboardGuard;
    private readonly IBackupService _backupService;
    private readonly BackupScheduler _backupScheduler;
    private readonly IExportService _exportService;
    private readonly EncryptedVaultFileManager _vaultFileManager;
    private readonly VaultKeyManager _keyManager;
    private readonly BruteForceGuard _bruteForceGuard;
    private readonly RecoveryKeyGenerator _recoveryKeyGenerator;
    private IWindowsHelloService? _windowsHelloService;
    private readonly AutoLockBehavior _autoLockBehavior;
    private readonly SemaphoreSlim _persistenceGate = new(1, 1);
    private readonly SemaphoreSlim _lockGate = new(1, 1);
    private readonly object _persistenceScheduleLock = new();
    private readonly NullVaultRepository _lockedRepository = new();
    private CancellationTokenSource? _persistDebounceCts;
    private Task _pendingPersistenceTask = Task.CompletedTask;
    private long _vaultChangeVersion;
    private long _persistedChangeVersion;

    private MainWindow? _mainWindow;
    private MainWindowViewModel? _mainWindowVM;
    private IVaultRepository? _currentRepository;
    private SqliteConnection? _currentConnection;
    private VaultFile? _currentVaultMetadata;
    private string? _currentVaultPath;
    private HwndSource? _mainWindowSource;
    private GlobalHotkeyService? _nativeHotkeyService;
    private AppConfig _config = new();
    private bool _isLocking;
    private bool _isHandlingMainWindowClose;
    private bool _allowMainWindowClose;
    private bool _windowIntegrationCleanedUp;
    private bool _disposed;
    private System.Windows.Forms.NotifyIcon? _trayIcon;

    public AppCoordinator(IServiceProvider services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));

        // Resolve all required services from DI
        _configService = services.GetRequiredService<IConfigService>();
        _cryptoService = services.GetRequiredService<ICryptoService>();
        _secureMemoryManager = services.GetRequiredService<ISecureMemoryManager>();
        _antiCaptureService = services.GetRequiredService<IAntiCaptureService>();
        _globalHotkeyService = services.GetRequiredService<IGlobalHotkeyService>();
        _eventBus = services.GetRequiredService<IEventBus>();
        _clipboardGuard = services.GetRequiredService<IClipboardGuard>();
        _backupService = services.GetRequiredService<IBackupService>();
        _backupScheduler = services.GetRequiredService<BackupScheduler>();
        _backupScheduler.BackupCompleted += OnBackupCompleted;
        _exportService = services.GetRequiredService<IExportService>();
        _vaultFileManager = services.GetRequiredService<EncryptedVaultFileManager>();
        _keyManager = services.GetRequiredService<VaultKeyManager>();
        _bruteForceGuard = services.GetRequiredService<BruteForceGuard>();
        _recoveryKeyGenerator = services.GetRequiredService<RecoveryKeyGenerator>();

        _autoLockBehavior = new AutoLockBehavior();
        _autoLockBehavior.IdleTimeoutReached += OnIdleTimeoutReached;
        _eventBus.Subscribe<VaultLockedEvent>(OnVaultLockRequested);
    }

    /// <summary>
    /// Entry point called from App.xaml.cs OnStartup.
    /// Loads config, creates MainWindow, and launches either setup wizard or unlock dialog.
    /// </summary>
    public void Start()
    {
        try
        {
            // Load configuration
            _config = _configService.Load();
            Log.Information("Configuration loaded from {ConfigPath}", _configService.ConfigFilePath);

            ApplyLanguageFromConfig();

            // Apply theme from config
            ApplyThemeFromConfig();

            // Create MainWindow with ViewModel
            CreateMainWindow();

            // Show main window
            _mainWindow!.Show();

            // Initialize integrations that require a real HWND.
            InitializeWindowIntegration();
            ApplyRuntimeSettings();
            _autoLockBehavior.Start();

            // Launch appropriate initial dialog
            if (_configService.IsFirstRun)
            {
                Log.Information("First run detected, launching setup wizard");
                ShowSetupWizard();
            }
            else
            {
                var lastVaultPath = _configService.GetLastOpenedVaultPath();
                if (!string.IsNullOrEmpty(lastVaultPath))
                {
                    Log.Information("Launching unlock dialog for last opened vault: {VaultPath}", lastVaultPath);
                    ShowUnlockDialog(lastVaultPath);
                }
                else
                {
                    Log.Information("No vault configured, launching setup wizard");
                    ShowSetupWizard();
                }
            }
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Failed to start application coordinator");
            MessageBox.Show(
                $"Application failed to start: {ex.Message}",
                "CipherVault - Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Application.Current.Shutdown(1);
        }
    }

    #region MainWindow Creation

    private void CreateMainWindow()
    {
        // Create child ViewModels with null repository (will be set after unlock)
        var vaultListVM = new VaultListViewModel(_lockedRepository);
        var entryListVM = new EntryListViewModel(_lockedRepository);
        var entryDetailVM = new EntryDetailViewModel(_lockedRepository, _clipboardGuard, _cryptoService, _keyManager);

        foreach (var vault in _config.Vaults)
        {
            vaultListVM.AddVault(vault.Name, vault.Path);
        }

        // Create MainWindowViewModel
        _mainWindowVM = new MainWindowViewModel(
            vaultListVM,
            entryListVM,
            entryDetailVM,
            _eventBus,
            _backupService,
            _globalHotkeyService);

        // Subscribe to MainWindowViewModel events
        _mainWindowVM.SettingsRequested += OnSettingsRequested;
        _mainWindowVM.GlobalSearchRequested += OnGlobalSearchRequested;
        _mainWindowVM.SetupWizardRequested += OnSetupWizardRequested;
        _mainWindowVM.VaultUnlockedRequested += OnVaultUnlockedRequested;
        _mainWindowVM.SwitchVaultRequested += OnSwitchVaultRequested;
        _mainWindowVM.ExportRequested += OnExportRequested;
        _mainWindowVM.ImportRequested += OnImportRequested;
        _mainWindowVM.RestoreBackupRequested += OnRestoreBackupRequested;

        // Create MainWindow and set DataContext
        _mainWindow = new MainWindow
        {
            DataContext = _mainWindowVM
        };

        _mainWindow.Closing += OnMainWindowClosing;
        _mainWindow.StateChanged += OnMainWindowStateChanged;
        _mainWindow.Deactivated += OnMainWindowDeactivated;
    }

    #endregion

    #region Event Handlers

    private void OnSettingsRequested(object? sender, EventArgs e)
    {
        try
        {
            bool windowsHelloWasEnabled = _config.Settings.Security.WindowsHelloEnabled;
            string[] configuredVaultPaths = _config.Vaults
                .Select(v => v.Path)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var settingsVM = new SettingsViewModel(_configService, GetWindowsHelloService());
            var settingsWindow = new SettingsWindow(settingsVM)
            {
                Owner = _mainWindow
            };
            _ = settingsVM.InitializeWindowsHelloAsync(_currentVaultPath);

            settingsVM.SettingsSaved += (_, _) =>
            {
                _config = _configService.Load();
                if (windowsHelloWasEnabled && !_config.Settings.Security.WindowsHelloEnabled)
                {
                    foreach (string vaultPath in configuredVaultPaths)
                        GetWindowsHelloService().RemoveStoredKey(vaultPath);
                }

                ApplyLanguageFromConfig();
                ApplyThemeFromConfig();
                ApplyRuntimeSettings();
            };

            settingsWindow.ShowDialog();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to open settings window");
        }
    }

    private void OnGlobalSearchRequested(object? sender, EventArgs e)
    {
        if (_currentRepository == null)
        {
            Log.Warning("Global search requested but no vault is unlocked");
            return;
        }

        try
        {
            var searchVM = new GlobalSearchViewModel(_currentRepository, _clipboardGuard, _cryptoService, _keyManager);
            var searchPopup = new GlobalSearchPopup
            {
                DataContext = searchVM,
                Owner = _mainWindow
            };

            searchVM.Closed += (_, _) => searchPopup.Close();

            searchVM.Initialize();
            ApplyCapturePreferenceOnSourceInitialized(searchPopup);
            searchPopup.Show();
            _ = searchVM.LoadInitialResultsAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to open global search popup");
        }
    }

    private void OnSetupWizardRequested(object? sender, EventArgs e)
    {
        ShowSetupWizard();
    }

    private void OnVaultUnlockedRequested(object? sender, VaultUnlockedEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.VaultPath))
        {
            ShowUnlockDialog(e.VaultPath);
        }
    }

    private async void OnSwitchVaultRequested(object? sender, VaultSwitchRequestedEventArgs e)
    {
        string? vaultPath = e.VaultPath;
        if (string.IsNullOrWhiteSpace(vaultPath))
        {
            var picker = new OpenFileDialog
            {
                Title = L.T("打开 CipherVault 保险库"),
                Filter = L.T("CipherVault 保险库 (*.cvault)|*.cvault|所有文件 (*.*)|*.*"),
                CheckFileExists = true,
                Multiselect = false,
            };

            if (picker.ShowDialog(_mainWindow) != true)
                return;

            vaultPath = Path.GetFullPath(picker.FileName);
            string name = Path.GetFileNameWithoutExtension(vaultPath);
            _configService.AddVault(name, vaultPath);
            _mainWindowVM?.VaultListVM.AddVault(name, vaultPath);
        }

        if (!File.Exists(vaultPath))
        {
            _configService.RemoveVault(vaultPath);
            MessageBox.Show(L.T("该保险库文件不存在，已从列表中移除。"), "CipherVault",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_keyManager.IsUnlocked &&
            !string.Equals(_currentVaultPath, vaultPath, StringComparison.OrdinalIgnoreCase))
            await LockCurrentVaultAsync(LockReason.VaultSwitch);

        if (_keyManager.IsUnlocked)
            return;

        _configService.SetLastOpenedVault(vaultPath);
        ShowUnlockDialog(vaultPath);
    }

    private async void OnImportRequested(object? sender, EventArgs e)
    {
        if (_currentRepository == null || !_keyManager.IsUnlocked)
        {
            Log.Warning("Import requested but vault is not unlocked");
            return;
        }

        try
        {
            var importService = new CsvImportService(_currentRepository, _cryptoService, _keyManager);
            var importVM = new ImportViewModel(importService, _currentRepository);
            var importWindow = new ImportWizardWindow(importVM)
            {
                Owner = _mainWindow
            };

            ApplyCapturePreferenceOnSourceInitialized(importWindow);

            if (importWindow.ShowDialog() == true && _mainWindowVM != null)
            {
                await _mainWindowVM.VaultListVM.LoadGroupsAsync();
                await _mainWindowVM.EntryListVM.RefreshAsync();
                _mainWindowVM.RefreshEntryCount();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to open import wizard");
            MessageBox.Show(
                L.F("导入失败：{0}", ex.Message),
                L.T("CipherVault - 导入"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void OnExportRequested(object? sender, EventArgs e)
    {
        if (_currentRepository == null || !_keyManager.IsUnlocked)
        {
            Log.Warning("Export requested but vault is not unlocked");
            return;
        }

        try
        {
            var exportVM = new ExportViewModel(_exportService, _currentRepository, _cryptoService, _keyManager);
            var exportDialog = new ExportDialog
            {
                Owner = _mainWindow
            };
            exportDialog.Initialize(exportVM);
            ApplyCapturePreferenceOnSourceInitialized(exportDialog);
            exportDialog.ShowDialog();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to open export dialog");
        }
    }

    private async void OnRestoreBackupRequested(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentVaultPath) || !_keyManager.IsUnlocked)
            return;

        string targetVaultPath = _currentVaultPath;
        var picker = new OpenFileDialog
        {
            Title = L.T("选择要恢复的 CipherVault 备份"),
            Filter = L.T("CipherVault 备份 (*.cvault)|*.cvault|所有文件 (*.*)|*.*"),
            CheckFileExists = true,
            Multiselect = false,
            InitialDirectory = string.IsNullOrWhiteSpace(_config.Settings.Backup.BackupDirectory)
                ? Path.Combine(Path.GetDirectoryName(targetVaultPath) ?? string.Empty, "backups")
                : _config.Settings.Backup.BackupDirectory,
        };
        if (picker.ShowDialog(_mainWindow) != true)
            return;

        string backupPath = Path.GetFullPath(picker.FileName);
        if (backupPath.Equals(Path.GetFullPath(targetVaultPath), StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(L.T("请选择备份文件，而不是当前保险库文件。"), "CipherVault",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (MessageBox.Show(
                L.T("恢复会用所选备份替换当前保险库。系统会先自动保存一份当前状态，是否继续？"),
                L.T("恢复备份"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        try
        {
            await LockCurrentVaultAsync(LockReason.Manual);
            await _backupService.RestoreFromBackupAsync(backupPath, targetVaultPath);
            ShowUnlockDialog(targetVaultPath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to restore vault backup {BackupPath}", backupPath);
            MessageBox.Show(L.F("恢复失败：{0}", ex.Message), "CipherVault",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    #endregion

    #region Setup Wizard

    private void ShowSetupWizard()
    {
        try
        {
            var wizardVM = new SetupWizardViewModel(_vaultFileManager, _recoveryKeyGenerator, _cryptoService);
            var wizardWindow = new SetupWizardWindow
            {
                DataContext = wizardVM,
                Owner = _mainWindow
            };

            wizardVM.SetupCompleted += OnSetupWizardCompleted;
            wizardVM.Cancelled += (_, _) => wizardWindow.Close();

            ApplyCapturePreferenceOnSourceInitialized(wizardWindow);
            wizardWindow.ShowDialog();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to open setup wizard");
        }
    }

    private void OnSetupWizardCompleted(object? sender, SetupCompletedEventArgs e)
    {
        try
        {
            Log.Information("Setup wizard completed. Vault: {VaultPath}", e.VaultFilePath);

            // Add vault to config
            _configService.AddVault(e.VaultName, e.VaultFilePath);
            _configService.SetLastOpenedVault(e.VaultFilePath);

            // Close wizard window
            if (sender is SetupWizardViewModel wizardVM)
            {
                var wizardWindow = Application.Current.Windows
                    .OfType<SetupWizardWindow>()
                    .FirstOrDefault(w => w.DataContext == wizardVM);
                wizardWindow?.Close();
            }

            // Show unlock dialog for the newly created vault
            ShowUnlockDialog(e.VaultFilePath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to handle setup wizard completion");
        }
    }

    #endregion

    #region Unlock Dialog

    private void ShowUnlockDialog(string vaultPath)
    {
        try
        {
            var helloService = _config.Settings.Security.WindowsHelloEnabled
                ? GetWindowsHelloService()
                : null;
            var unlockVM = new UnlockViewModel(_vaultFileManager, _bruteForceGuard, _keyManager, helloService);
            unlockVM.SetVaultFilePath(vaultPath);

            // Check Windows Hello availability asynchronously
            _ = unlockVM.CheckWindowsHelloAvailabilityAsync();

            var unlockDialog = new UnlockDialog
            {
                DataContext = unlockVM,
                Owner = _mainWindow,
                ProtectSensitiveWindow = window =>
                    ApplyCapturePreferenceOnSourceInitialized(window),
            };

            unlockVM.Unlocked += OnVaultUnlocked;
            unlockVM.Cancelled += (_, _) => unlockDialog.Close();

            ApplyCapturePreferenceOnSourceInitialized(unlockDialog);
            unlockDialog.ShowDialog();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to open unlock dialog for {VaultPath}", vaultPath);
        }
    }

    private async void OnVaultUnlocked(object? sender, VaultUnlockCompletedEventArgs e)
    {
        try
        {
            Log.Information("Vault unlocked successfully: {VaultPath}", e.VaultFilePath);

            if (_currentRepository != null)
            {
                // UnlockViewModel has already installed the new key at this point, so do not
                // attempt to save the stale session with that key. Normal vault switching
                // locks the previous session before opening this dialog.
                Log.Warning("Replacing a stale vault session during unlock");
                if (_currentRepository is IDisposable staleRepository)
                {
                    staleRepository.Dispose();
                }
                _currentRepository = null;
                _currentConnection = null;
                _currentVaultMetadata = null;
                _currentVaultPath = null;
            }

            _currentConnection = e.Connection;
            _currentVaultMetadata = e.Metadata;
            _currentVaultPath = e.VaultFilePath;
            e.Metadata.FailedAttempts = 0;
            e.Metadata.LockoutUntil = null;

            var repository = new VaultRepository(
                e.Connection,
                () => ScheduleVaultPersistenceAsync(
                    e.Connection,
                    e.Metadata,
                    e.VaultFilePath,
                    TimeSpan.FromMilliseconds(600)),
                () => ScheduleVaultPersistenceAsync(
                    e.Connection,
                    e.Metadata,
                    e.VaultFilePath,
                    TimeSpan.FromSeconds(20)));
            _currentRepository = repository;
            await repository.PurgeDeletedEntriesAsync(olderThanDays: 30);

            // Update last opened vault in config
            _configService.SetLastOpenedVault(e.VaultFilePath);

            if (_mainWindowVM != null)
            {
                _mainWindowVM.SetRepository(repository);
                _mainWindowVM.VaultListVM.AddVault(
                    _config.Vaults.FirstOrDefault(v =>
                        v.Path.Equals(e.VaultFilePath, StringComparison.OrdinalIgnoreCase))?.Name
                        ?? Path.GetFileNameWithoutExtension(e.VaultFilePath),
                    e.VaultFilePath);

                await _mainWindowVM.OnVaultUnlockedAsync(e.VaultFilePath);
            }

            // Close unlock dialog
            if (sender is UnlockViewModel unlockVM)
            {
                var unlockDialog = Application.Current.Windows
                    .OfType<UnlockDialog>()
                    .FirstOrDefault(w => w.DataContext == unlockVM);
                unlockDialog?.Close();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to handle vault unlock");
            await LockCurrentVaultAsync(LockReason.Manual);
            MessageBox.Show(
                $"Failed to unlock vault: {ex.Message}",
                "CipherVault - Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    #endregion

    #region Vault Locking

    private async void OnVaultLockRequested(VaultLockedEvent evt)
    {
        try
        {
            await LockCurrentVaultAsync(evt.Reason);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to lock vault after {LockReason}", evt.Reason);
        }
    }

    private void OnIdleTimeoutReached()
    {
        if (!_keyManager.IsUnlocked || _isLocking)
            return;

        _eventBus.Publish(new VaultLockedEvent
        {
            Reason = LockReason.IdleTimeout,
            VaultFilePath = _currentVaultPath
        });
    }

    private async Task<bool> PersistVaultAsync(
        SqliteConnection connection,
        VaultFile metadata,
        string vaultPath)
    {
        await _persistenceGate.WaitAsync();
        try
        {
            // A delayed mutation from an old session must never be encrypted with the
            // key belonging to a newer vault.
            if (!ReferenceEquals(connection, _currentConnection)
                || !ReferenceEquals(metadata, _currentVaultMetadata)
                || !string.Equals(vaultPath, _currentVaultPath, StringComparison.OrdinalIgnoreCase)
                || connection.State != ConnectionState.Open
                || !_keyManager.IsUnlocked)
            {
                return false;
            }

            byte[] key = _keyManager.GetKey();
            try
            {
                _vaultFileManager.SaveAndEncrypt(vaultPath, connection, metadata, key);
            }
            finally
            {
                Array.Clear(key);
            }

            if (_config.Settings.Backup.AutoBackup)
            {
                _backupScheduler.ScheduleBackup(vaultPath);
            }

            Log.Debug("Vault changes persisted to {VaultPath}", vaultPath);
            return true;
        }
        finally
        {
            _persistenceGate.Release();
        }
    }

    private Task ScheduleVaultPersistenceAsync(
        SqliteConnection connection,
        VaultFile metadata,
        string vaultPath,
        TimeSpan delay)
    {
        lock (_persistenceScheduleLock)
        {
            long version = ++_vaultChangeVersion;
            _persistDebounceCts?.Cancel();

            var cancellation = new CancellationTokenSource();
            _persistDebounceCts = cancellation;
            _pendingPersistenceTask = PersistAfterDebounceAsync(
                connection,
                metadata,
                vaultPath,
                version,
                delay,
                cancellation);
        }

        // Repository mutations commit immediately to the in-memory SQLite
        // transaction. Expensive whole-vault encryption is deliberately deferred.
        return Task.CompletedTask;
    }

    private async Task PersistAfterDebounceAsync(
        SqliteConnection connection,
        VaultFile metadata,
        string vaultPath,
        long version,
        TimeSpan delay,
        CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(delay, cancellation.Token);
            bool persisted = await PersistVaultAsync(connection, metadata, vaultPath);

            if (persisted)
            {
                lock (_persistenceScheduleLock)
                {
                    _persistedChangeVersion = Math.Max(_persistedChangeVersion, version);
                }
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // A newer change or an explicit flush superseded this write.
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Deferred vault save failed for {VaultPath}", vaultPath);
        }
        finally
        {
            lock (_persistenceScheduleLock)
            {
                if (ReferenceEquals(_persistDebounceCts, cancellation))
                    _persistDebounceCts = null;
            }
            cancellation.Dispose();
        }
    }

    private async Task FlushPendingPersistenceAsync(
        SqliteConnection connection,
        VaultFile metadata,
        string vaultPath)
    {
        Task pending;
        long targetVersion;

        lock (_persistenceScheduleLock)
        {
            _persistDebounceCts?.Cancel();
            pending = _pendingPersistenceTask;
            targetVersion = _vaultChangeVersion;
        }

        await pending;

        bool hasUnpersistedChanges;
        lock (_persistenceScheduleLock)
        {
            hasUnpersistedChanges = _persistedChangeVersion < targetVersion;
        }

        if (!hasUnpersistedChanges)
            return;

        Exception? lastFailure = null;
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                bool persisted = await PersistVaultAsync(connection, metadata, vaultPath);
                if (!persisted)
                    throw new InvalidOperationException("The active vault session changed before pending data could be saved.");

                lock (_persistenceScheduleLock)
                {
                    _persistedChangeVersion = Math.Max(_persistedChangeVersion, targetVersion);
                }
                return;
            }
            catch (Exception ex) when (attempt < 3)
            {
                lastFailure = ex;
                await Task.Delay(TimeSpan.FromMilliseconds(150 * attempt));
            }
            catch (Exception ex)
            {
                lastFailure = ex;
            }
        }

        throw new IOException("Failed to persist pending vault changes after three attempts.", lastFailure);
    }

    private async Task LockCurrentVaultAsync(LockReason reason)
    {
        await _lockGate.WaitAsync();
        Exception? persistenceFailure = null;
        try
        {
            _isLocking = true;
            var repository = _currentRepository;
            var connection = _currentConnection;
            var metadata = _currentVaultMetadata;
            var vaultPath = _currentVaultPath;

            if (connection != null
                && metadata != null
                && !string.IsNullOrEmpty(vaultPath)
                && _keyManager.IsUnlocked)
            {
                try
                {
                    await FlushPendingPersistenceAsync(connection, metadata, vaultPath);
                }
                catch (Exception ex)
                {
                    // Security still requires locking and destroying the key. Preserve
                    // the error so the user is explicitly warned after the lock completes.
                    persistenceFailure = ex;
                    Log.Error(ex, "Final vault save failed while locking {VaultPath}", vaultPath);
                }
            }

            _mainWindowVM?.SetRepository(_lockedRepository);
            _mainWindowVM?.RefreshEntryCount();

            _currentRepository = null;
            _currentConnection = null;
            _currentVaultMetadata = null;
            _currentVaultPath = null;

            if (repository is IDisposable disposable)
            {
                disposable.Dispose();
            }

            if (_keyManager.IsUnlocked)
            {
                _keyManager.DestroyKey();
            }

            Log.Information("Vault locked ({LockReason})", reason);

            if (persistenceFailure != null)
            {
                MessageBox.Show(
                    L.T("保险库已安全锁定，但最近的更改未能写入磁盘。请检查磁盘空间和文件权限后重新打开并确认数据。"),
                    "CipherVault",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        finally
        {
            _isLocking = false;
            _lockGate.Release();
        }
    }

    #endregion

    #region Windows Integration

    private const int WmHotkey = 0x0312;

    private void InitializeWindowIntegration()
    {
        if (_mainWindow == null)
            return;

        var windowHelper = new WindowInteropHelper(_mainWindow);
        var windowHandle = windowHelper.Handle;
        if (windowHandle == IntPtr.Zero)
        {
            windowHandle = windowHelper.EnsureHandle();
        }

        _nativeHotkeyService = _globalHotkeyService as GlobalHotkeyService;
        if (_nativeHotkeyService == null)
        {
            Log.Warning("The configured global hotkey service does not expose WPF HWND integration");
            return;
        }

        _nativeHotkeyService.SetWindowHandle(windowHandle);
        _mainWindowSource = HwndSource.FromHwnd(windowHandle);
        _mainWindowSource?.AddHook(MainWindowWndProc);
    }

    private IntPtr MainWindowWndProc(
        IntPtr hwnd,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (message == WmHotkey)
        {
            _nativeHotkeyService?.HandleHotkeyMessage(wParam.ToInt32());
            handled = true;
        }

        return IntPtr.Zero;
    }

    private void ApplyRuntimeSettings()
    {
        var security = _config.Settings.Security;
        _clipboardGuard.AutoClearSeconds = Math.Clamp(security.ClipboardClearSeconds, 5, 60);
        _autoLockBehavior.TimeoutMinutes = Math.Clamp(security.IdleTimeoutMinutes, 1, 60);
        _autoLockBehavior.LockOnSystemLock = security.LockOnSystemLock;
        _backupService.Configure(
            _config.Settings.Backup.BackupDirectory,
            _config.Settings.Backup.MaxBackups);
        ConfigureTrayIntegration();
        ConfigureWindowsStartup(_config.Settings.Tray.StartWithWindows);

        var hotkey = string.IsNullOrWhiteSpace(_config.Settings.Hotkey.GlobalSearch)
            ? "Ctrl+Shift+P"
            : _config.Settings.Hotkey.GlobalSearch.Trim();
        if (_mainWindowVM != null)
        {
            _mainWindowVM.GlobalSearchHotkey = hotkey;
            _mainWindowVM.SidebarWidth = new GridLength(Math.Clamp(_config.Settings.Appearance.SidebarWidth, 160, 360));
            _mainWindowVM.EntryListWidth = new GridLength(Math.Clamp(_config.Settings.Appearance.ListWidth, 240, 520));
        }
        UI.Themes.ThemeManager.Instance.ApplyFontSize(_config.Settings.Appearance.FontSize);

        if (_mainWindow != null)
        {
            var windowHandle = new WindowInteropHelper(_mainWindow).Handle;
            if (windowHandle != IntPtr.Zero)
            {
                if (security.AntiCapture)
                {
                    _antiCaptureService.EnableAntiCapture(windowHandle);
                }
                else
                {
                    _antiCaptureService.DisableAntiCapture(windowHandle);
                }
            }
        }

        try
        {
            _globalHotkeyService.Unregister();
            if (_nativeHotkeyService != null)
            {
                _globalHotkeyService.Register(hotkey);
                if (!_globalHotkeyService.IsRegistered)
                {
                    Log.Warning("Global hotkey {GlobalHotkey} could not be registered", hotkey);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to register global hotkey {GlobalHotkey}", hotkey);
        }
    }

    private void ApplyCapturePreferenceOnSourceInitialized(Window window)
    {
        void ApplyProtection(object? sender, EventArgs args)
        {
            window.SourceInitialized -= ApplyProtection;

            var windowHandle = new WindowInteropHelper(window).Handle;
            if (windowHandle == IntPtr.Zero)
                return;

            if (_config.Settings.Security.AntiCapture)
            {
                _antiCaptureService.EnableAntiCapture(windowHandle);
            }
            else
            {
                _antiCaptureService.DisableAntiCapture(windowHandle);
            }
        }

        if (new WindowInteropHelper(window).Handle != IntPtr.Zero)
        {
            ApplyProtection(window, EventArgs.Empty);
        }
        else
        {
            window.SourceInitialized += ApplyProtection;
        }
    }

    private IWindowsHelloService GetWindowsHelloService()
        => _windowsHelloService ??= _services.GetRequiredService<IWindowsHelloService>();

    private void OnMainWindowStateChanged(object? sender, EventArgs e)
    {
        if (_mainWindow?.WindowState == WindowState.Minimized
            && _config.Settings.Security.LockOnMinimize)
        {
            RequestWindowLock();
        }
    }

    private void OnMainWindowDeactivated(object? sender, EventArgs e)
    {
        if (!_config.Settings.Security.LockOnWindowBlur || _mainWindow == null)
            return;

        _mainWindow.Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.ApplicationIdle,
            new Action(() =>
            {
                if (_mainWindow == null || _mainWindow.IsActive)
                    return;

                var hasActiveOwnedWindow = Application.Current.Windows
                    .OfType<Window>()
                    .Any(window => window != _mainWindow
                        && window.Owner == _mainWindow
                        && window.IsActive);
                if (!hasActiveOwnedWindow)
                {
                    RequestWindowLock();
                }
            }));
    }

    private void RequestWindowLock()
    {
        if (!_keyManager.IsUnlocked || _isLocking)
            return;

        _eventBus.Publish(new VaultLockedEvent
        {
            Reason = LockReason.WindowBlur,
            VaultFilePath = _currentVaultPath
        });
    }

    private async void OnMainWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_allowMainWindowClose)
        {
            CleanupWindowIntegration();
            return;
        }

        e.Cancel = true;
        if (_isHandlingMainWindowClose)
            return;

        _isHandlingMainWindowClose = true;
        try
        {
            string closeBehavior = _config.Settings.Tray.CloseBehavior?.ToLowerInvariant() switch
            {
                "tray" => "tray",
                "exit" => "exit",
                _ => "ask",
            };

            if (closeBehavior == "ask")
            {
                if (_mainWindow == null)
                    return;

                var dialog = new CloseBehaviorDialog
                {
                    Owner = _mainWindow,
                };
                if (dialog.ShowDialog() != true)
                    return;

                closeBehavior = dialog.SelectedChoice == MainWindowCloseChoice.ExitApplication
                    ? "exit"
                    : "tray";

                if (dialog.RememberSelection)
                {
                    _config.Settings.Tray.CloseBehavior = closeBehavior;
                    _config.Settings.Tray.MinimizeToTray = closeBehavior == "tray";
                    if (closeBehavior == "tray")
                        _config.Settings.Tray.ShowTrayIcon = true;

                    try
                    {
                        await _configService.SaveAsync(_config);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Failed to remember the main-window close behavior");
                    }
                }
            }

            if (closeBehavior == "tray")
            {
                ConfigureTrayIntegration(forceShow: true);
                if (_currentRepository != null || _keyManager.IsUnlocked)
                    await LockCurrentVaultAsync(LockReason.Manual);
                _mainWindow?.Hide();
                return;
            }

            if (_currentRepository != null || _keyManager.IsUnlocked)
                await LockCurrentVaultAsync(LockReason.Manual);

            _allowMainWindowClose = true;
            // This handler is already inside Window.Close(). Calling Close()
            // synchronously again throws InvalidOperationException in WPF.
            // Let the cancelled closing event unwind before issuing the final close.
            Window? window = _mainWindow;
            window?.Dispatcher.BeginInvoke(new Action(window.Close));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to apply the selected main-window close behavior");
        }
        finally
        {
            _isHandlingMainWindowClose = false;
        }
    }

    private void CleanupWindowIntegration()
    {
        if (_windowIntegrationCleanedUp)
            return;

        _windowIntegrationCleanedUp = true;
        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }
        _autoLockBehavior.IdleTimeoutReached -= OnIdleTimeoutReached;
        _autoLockBehavior.Dispose();
        _eventBus.Unsubscribe<VaultLockedEvent>(OnVaultLockRequested);

        try
        {
            _globalHotkeyService.Unregister();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to unregister the global hotkey during shutdown");
        }

        if (_mainWindowSource != null)
        {
            _mainWindowSource.RemoveHook(MainWindowWndProc);
            _mainWindowSource = null;
        }

        if (_mainWindow != null)
        {
            _mainWindow.Closing -= OnMainWindowClosing;
            _mainWindow.StateChanged -= OnMainWindowStateChanged;
            _mainWindow.Deactivated -= OnMainWindowDeactivated;
        }
    }

    private void ConfigureTrayIntegration(bool forceShow = false)
    {
        bool shouldShow = forceShow || _config.Settings.Tray.ShowTrayIcon;
        if (!shouldShow)
        {
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
            }
            return;
        }

        if (_trayIcon != null)
        {
            _trayIcon.Visible = true;
            return;
        }

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add(L.T("打开 CipherVault"), null, (_, _) => ShowMainWindowFromTray());
        menu.Items.Add(L.T("锁定保险库"), null, (_, _) =>
        {
            if (_keyManager.IsUnlocked)
                _eventBus.Publish(new VaultLockedEvent { Reason = LockReason.Manual, VaultFilePath = _currentVaultPath });
        });
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add(L.T("退出"), null, async (_, _) => await ExitFromTrayAsync());

        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Text = "CipherVault",
            Icon = System.Drawing.SystemIcons.Shield,
            ContextMenuStrip = menu,
            Visible = true,
        };
        _trayIcon.DoubleClick += (_, _) => ShowMainWindowFromTray();
    }

    private void ShowMainWindowFromTray()
    {
        if (_mainWindow == null)
            return;
        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();

        if (!_keyManager.IsUnlocked &&
            _config.Settings.Tray.DoubleClickAction.Equals("openAndUnlock", StringComparison.OrdinalIgnoreCase) &&
            !Application.Current.Windows.OfType<UnlockDialog>().Any())
        {
            string? path = _currentVaultPath ?? _configService.GetLastOpenedVaultPath();
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                ShowUnlockDialog(path);
        }
    }

    private async Task ExitFromTrayAsync()
    {
        try
        {
            if (_currentRepository != null || _keyManager.IsUnlocked)
                await LockCurrentVaultAsync(LockReason.Manual);
        }
        finally
        {
            _allowMainWindowClose = true;
            _mainWindow?.Close();
        }
    }

    private static void ConfigureWindowsStartup(bool enabled)
    {
        try
        {
            using var runKey = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run",
                writable: true);
            if (runKey == null)
                return;

            if (enabled && !string.IsNullOrWhiteSpace(Environment.ProcessPath))
                runKey.SetValue("CipherVault", $"\"{Environment.ProcessPath}\"");
            else
                runKey.DeleteValue("CipherVault", throwOnMissingValue: false);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to update Windows startup registration");
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _backupScheduler.BackupCompleted -= OnBackupCompleted;
        try
        {
            if (_currentRepository != null || _keyManager.IsUnlocked)
            {
                LockCurrentVaultAsync(LockReason.Manual).GetAwaiter().GetResult();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to close the active vault while disposing the application coordinator");

            if (_currentRepository is IDisposable repository)
            {
                repository.Dispose();
            }
            else
            {
                _currentConnection?.Dispose();
            }

            _currentRepository = null;
            _currentConnection = null;
            _currentVaultMetadata = null;
            _currentVaultPath = null;
            if (_keyManager.IsUnlocked)
            {
                _keyManager.DestroyKey();
            }
        }
        finally
        {
            CleanupWindowIntegration();
        }
    }

    #endregion

    #region Theme Management

    private void OnBackupCompleted(string vaultPath, DateTime timestamp)
    {
        if (!string.Equals(vaultPath, _currentVaultPath, StringComparison.OrdinalIgnoreCase))
            return;

        Application.Current.Dispatcher.BeginInvoke(() => _mainWindowVM?.RecordBackup(timestamp));
    }

    private void ApplyThemeFromConfig()
    {
        try
        {
            var themeString = _config.Settings.Appearance.Theme;
            if (Enum.TryParse<Core.Enums.ThemeMode>(themeString, true, out var themeMode))
            {
                UI.Themes.ThemeManager.Instance.CurrentMode = themeMode;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to apply theme from config");
        }
    }

    private void ApplyLanguageFromConfig()
    {
        try
        {
            UI.Localization.LocalizationManager.Instance.CurrentLanguage =
                UI.Localization.LocalizationManager.NormalizeLanguage(
                    _config.Settings.Appearance.Language);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to apply language from config");
        }
    }

    #endregion
}
