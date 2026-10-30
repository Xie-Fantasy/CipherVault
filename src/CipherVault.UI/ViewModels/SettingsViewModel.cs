using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CipherVault.Core.Config;
using CipherVault.Core.Enums;
using CipherVault.Core.Interfaces;
using CipherVault.Infrastructure.Security;
using CipherVault.UI.Localization;
using CipherVault.UI.Themes;

namespace CipherVault.UI.ViewModels;

/// <summary>
/// ViewModel for the settings page.
/// Manages all user-configurable settings across security, appearance, backup, hotkey, and tray categories.
/// Settings are loaded from and saved to a JSON configuration file.
/// </summary>
public partial class SettingsViewModel : ViewModelBase
{
    private readonly IConfigService _configService;
    private readonly IWindowsHelloService? _windowsHelloService;
    private AppConfig _config;
    private ThemeMode _persistedTheme = ThemeMode.System;
    private string _persistedLanguage = LocalizationManager.SimplifiedChinese;
    private string _persistedFontSize = "medium";
    private bool _isLoadingConfig;

    /// <summary>
    /// Occurs when settings have been saved, allowing other components to react.
    /// </summary>
    public event EventHandler? SettingsSaved;

    /// <summary>
    /// Occurs when the settings dialog should be closed.
    /// </summary>
    public event EventHandler? CloseRequested;

    public event EventHandler? BrowseBackupDirectoryRequested;

    public SettingsViewModel(
        IConfigService configService,
        IWindowsHelloService? windowsHelloService = null)
    {
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _windowsHelloService = windowsHelloService;
        _config = new AppConfig();

        // Load config from disk
        LoadConfig();
        ApplyConfigToProperties();
    }

    #region Security Settings

    /// <summary>
    /// Idle timeout in minutes before auto-locking the vault.
    /// </summary>
    [ObservableProperty]
    private int _idleTimeoutMinutes = 5;

    /// <summary>
    /// Whether to lock the vault when the system is locked (Win+L).
    /// </summary>
    [ObservableProperty]
    private bool _lockOnSystemLock = true;

    /// <summary>
    /// Whether to lock the vault when the window loses focus.
    /// </summary>
    [ObservableProperty]
    private bool _lockOnWindowBlur;

    /// <summary>
    /// Whether to lock the vault when the window is minimized.
    /// </summary>
    [ObservableProperty]
    private bool _lockOnMinimize;

    /// <summary>
    /// Seconds before the clipboard is automatically cleared after copying.
    /// </summary>
    [ObservableProperty]
    private int _clipboardClearSeconds = 15;

    /// <summary>
    /// Whether anti-capture (screenshot protection) is enabled.
    /// </summary>
    [ObservableProperty]
    private bool _antiCaptureEnabled;

    /// <summary>
    /// Whether Windows Hello biometric unlock is enabled.
    /// </summary>
    [ObservableProperty]
    private bool _windowsHelloEnabled;

    [ObservableProperty]
    private bool _windowsHelloAvailable;

    [ObservableProperty]
    private string _windowsHelloStatus = L.T("正在检查本机 Windows Hello…");

    partial void OnWindowsHelloEnabledChanged(bool value)
    {
        if (_isLoadingConfig || !WindowsHelloAvailable)
            return;

        WindowsHelloStatus = value
            ? L.T("保存后，首次使用主密码解锁时会将当前保险库安全绑定到本机 Windows Hello。")
            : L.T("关闭并保存后，将移除已绑定的 Windows Hello 快速解锁密钥。");
    }

    #endregion

    #region Appearance Settings

    /// <summary>
    /// Selected theme mode (Light, Dark, or System).
    /// </summary>
    [ObservableProperty]
    private ThemeMode _selectedTheme = ThemeMode.System;

    partial void OnSelectedThemeChanged(ThemeMode value)
    {
        if (!_isLoadingConfig)
            ThemeManager.Instance.CurrentMode = value;
    }

    /// <summary>
    /// Font size setting ("small", "medium", "large").
    /// </summary>
    [ObservableProperty]
    private string _fontSize = "medium";

    partial void OnFontSizeChanged(string value)
    {
        if (!_isLoadingConfig)
            ThemeManager.Instance.ApplyFontSize(value);
    }

    [ObservableProperty]
    private string _selectedLanguage = LocalizationManager.SimplifiedChinese;

    partial void OnSelectedLanguageChanged(string value)
    {
        if (_isLoadingConfig)
            return;

        LocalizationManager manager = LocalizationManager.Instance;
        manager.CurrentLanguage = value;
        WindowsHelloStatus = manager.Translate(WindowsHelloStatus);
        if (!string.IsNullOrEmpty(StatusMessage))
            StatusMessage = manager.Translate(StatusMessage);
    }

    #endregion

    #region Backup Settings

    /// <summary>
    /// Whether automatic backup is enabled after each modification.
    /// </summary>
    [ObservableProperty]
    private bool _autoBackup = true;

    /// <summary>
    /// Custom backup directory path. Empty string means default (same directory as .cvault file).
    /// </summary>
    [ObservableProperty]
    private string _backupDirectory = string.Empty;

    /// <summary>
    /// Maximum number of backup files to retain.
    /// </summary>
    [ObservableProperty]
    private int _maxBackups = 100;

    /// <summary>
    /// Backup retention policy ("standard", "aggressive", "minimal").
    /// </summary>
    [ObservableProperty]
    private string _retentionPolicy = "standard";

    #endregion

    #region Hotkey Settings

    /// <summary>
    /// Hotkey combination for the global search popup.
    /// </summary>
    [ObservableProperty]
    private string _globalSearchHotkey = "Ctrl+Shift+P";

    #endregion

    #region Tray Settings

    /// <summary>
    /// Action taken when the main-window close button is clicked:
    /// "ask", "tray", or "exit".
    /// </summary>
    [ObservableProperty]
    private string _closeBehavior = "ask";

    /// <summary>
    /// Whether closing the main window minimizes to the system tray instead of exiting.
    /// Retained for configuration compatibility; <see cref="CloseBehavior"/> is the UI setting.
    /// </summary>
    [ObservableProperty]
    private bool _minimizeToTray = true;

    /// <summary>
    /// Whether CipherVault starts automatically with Windows.
    /// </summary>
    [ObservableProperty]
    private bool _startWithWindows;

    /// <summary>
    /// Whether the system tray icon is visible.
    /// </summary>
    [ObservableProperty]
    private bool _showTrayIcon = true;

    /// <summary>
    /// Action when the tray icon is double-clicked ("open" or "openAndUnlock").
    /// </summary>
    [ObservableProperty]
    private string _doubleClickAction = "openAndUnlock";

    #endregion

    #region Feedback

    /// <summary>
    /// Status message shown after saving settings.
    /// </summary>
    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _statusIsError;

    #endregion

    #region Commands

    public async Task InitializeWindowsHelloAsync(string? currentVaultPath)
    {
        if (_windowsHelloService == null)
        {
            WindowsHelloAvailable = false;
            WindowsHelloStatus = L.T("当前环境未提供 Windows Hello 服务。");
            return;
        }

        try
        {
            WindowsHelloAvailable = await _windowsHelloService.IsAvailableAsync();
            if (!WindowsHelloAvailable)
            {
                WindowsHelloStatus = L.T("本机尚未配置 Windows Hello，请先在 Windows 登录选项中设置 PIN、面容或指纹。");
                return;
            }

            bool isBound = !string.IsNullOrWhiteSpace(currentVaultPath) &&
                           _windowsHelloService.HasStoredKey(currentVaultPath);
            WindowsHelloStatus = isBound
                ? L.T("当前保险库已安全绑定到本机 Windows Hello。")
                : WindowsHelloEnabled
                    ? L.T("当前保险库尚未绑定；请保存后锁定，再使用主密码解锁一次完成绑定。")
                    : L.T("启用后可使用 Windows PIN、面容或指纹快速解锁；主密码仍可作为兜底。");
        }
        catch (Exception ex)
        {
            WindowsHelloAvailable = false;
            WindowsHelloStatus = L.F("无法检查 Windows Hello：{0}", ex.Message);
        }
    }

    /// <summary>
    /// Saves all settings to the configuration file.
    /// </summary>
    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!GlobalHotkeyService.IsValidHotkey(GlobalSearchHotkey))
        {
            StatusIsError = true;
            StatusMessage = L.T("快捷键格式无效，请使用例如 Ctrl+Shift+P 的格式。");
            return;
        }

        if (WindowsHelloEnabled && _windowsHelloService != null && !WindowsHelloAvailable)
        {
            StatusIsError = true;
            StatusMessage = L.T("本机未配置可用的 Windows Hello，无法启用快速解锁。");
            return;
        }

        StatusIsError = false;
        await ExecuteWithErrorHandlingAsync(async () =>
        {
            ApplyPropertiesToConfig();

            // Validate and apply appearance resources before persisting. If a packaged
            // resource is unavailable, no partially-saved configuration is left behind.
            ThemeManager.Instance.CurrentMode = SelectedTheme;
            ThemeManager.Instance.ApplyFontSize(FontSize);
            LocalizationManager.Instance.CurrentLanguage = SelectedLanguage;

            await SaveConfigAsync();
            _persistedTheme = SelectedTheme;
            _persistedLanguage = SelectedLanguage;
            _persistedFontSize = FontSize;

            StatusMessage = L.T("设置已保存。");

            SettingsSaved?.Invoke(this, EventArgs.Empty);
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }, "saving settings");
    }

    /// <summary>
    /// Resets all settings to their default values.
    /// </summary>
    [RelayCommand]
    private void ResetToDefaults()
    {
        // Security
        IdleTimeoutMinutes = 5;
        LockOnSystemLock = true;
        LockOnWindowBlur = false;
        LockOnMinimize = false;
        ClipboardClearSeconds = 15;
        AntiCaptureEnabled = false;
        WindowsHelloEnabled = false;

        // Appearance
        SelectedTheme = ThemeMode.System;
        FontSize = "medium";
        SelectedLanguage = LocalizationManager.SimplifiedChinese;

        // Backup
        AutoBackup = true;
        BackupDirectory = string.Empty;
        MaxBackups = 100;
        RetentionPolicy = "standard";

        // Hotkey
        GlobalSearchHotkey = "Ctrl+Shift+P";

        // Tray
        CloseBehavior = "ask";
        MinimizeToTray = false;
        StartWithWindows = false;
        ShowTrayIcon = true;
        DoubleClickAction = "openAndUnlock";

        StatusIsError = false;
        StatusMessage = L.T("设置已恢复默认值。");
    }

    /// <summary>
    /// Opens a folder browser to select a backup directory.
    /// The actual dialog is shown by the View layer.
    /// </summary>
    [RelayCommand]
    private void BrowseBackupDirectory()
    {
        BrowseBackupDirectoryRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Closes the settings dialog.
    /// </summary>
    [RelayCommand]
    private void Close()
    {
        // Theme selection is previewed immediately. Closing without saving restores
        // the last persisted theme instead of leaving an unsaved session-only state.
        ThemeManager.Instance.CurrentMode = _persistedTheme;
        ThemeManager.Instance.ApplyFontSize(_persistedFontSize);
        LocalizationManager.Instance.CurrentLanguage = _persistedLanguage;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    #endregion

    #region Configuration I/O

    private void LoadConfig()
    {
        try
        {
            _config = _configService.Load();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning("Failed to load config, using defaults: {0}", ex.Message);
            _config = new AppConfig();
        }
    }

    private async Task SaveConfigAsync()
    {
        try
        {
            await _configService.SaveAsync(_config);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceError("Failed to save config: {0}", ex);
            throw;
        }
    }

    private void ApplyConfigToProperties()
    {
        var s = _config.Settings;
        _isLoadingConfig = true;
        try
        {

        // Security
        IdleTimeoutMinutes = s.Security.IdleTimeoutMinutes;
        LockOnSystemLock = s.Security.LockOnSystemLock;
        LockOnWindowBlur = s.Security.LockOnWindowBlur;
        LockOnMinimize = s.Security.LockOnMinimize;
        ClipboardClearSeconds = s.Security.ClipboardClearSeconds;
        AntiCaptureEnabled = s.Security.AntiCapture;
        WindowsHelloEnabled = s.Security.WindowsHelloEnabled;

        // Appearance
        SelectedTheme = Enum.TryParse<ThemeMode>(s.Appearance.Theme, true, out var theme)
            ? theme
            : ThemeMode.System;
        _persistedTheme = SelectedTheme;
        FontSize = s.Appearance.FontSize;
        _persistedFontSize = FontSize;
        SelectedLanguage = LocalizationManager.NormalizeLanguage(s.Appearance.Language);
        _persistedLanguage = SelectedLanguage;

        // Backup
        AutoBackup = s.Backup.AutoBackup;
        BackupDirectory = s.Backup.BackupDirectory;
        MaxBackups = s.Backup.MaxBackups;
        RetentionPolicy = s.Backup.RetentionPolicy;

        // Hotkey
        GlobalSearchHotkey = s.Hotkey.GlobalSearch;

        // Tray
        CloseBehavior = s.Tray.CloseBehavior;
        MinimizeToTray = CloseBehavior == "tray";
        StartWithWindows = s.Tray.StartWithWindows;
        ShowTrayIcon = s.Tray.ShowTrayIcon;
        DoubleClickAction = s.Tray.DoubleClickAction;
        }
        finally
        {
            _isLoadingConfig = false;
        }
    }

    private void ApplyPropertiesToConfig()
    {
        var s = _config.Settings;

        // Security
        s.Security.IdleTimeoutMinutes = Math.Clamp(IdleTimeoutMinutes, 1, 60);
        s.Security.LockOnSystemLock = LockOnSystemLock;
        s.Security.LockOnWindowBlur = LockOnWindowBlur;
        s.Security.LockOnMinimize = LockOnMinimize;
        s.Security.ClipboardClearSeconds = Math.Clamp(ClipboardClearSeconds, 5, 60);
        s.Security.AntiCapture = AntiCaptureEnabled;
        s.Security.WindowsHelloEnabled = WindowsHelloEnabled;

        // Appearance
        s.Appearance.Theme = SelectedTheme.ToString().ToLowerInvariant();
        s.Appearance.FontSize = FontSize;
        s.Appearance.Language = LocalizationManager.NormalizeLanguage(SelectedLanguage);

        // Backup
        s.Backup.AutoBackup = AutoBackup;
        s.Backup.BackupDirectory = BackupDirectory;
        s.Backup.MaxBackups = Math.Max(MaxBackups, 1);
        s.Backup.RetentionPolicy = RetentionPolicy;

        // Hotkey
        s.Hotkey.GlobalSearch = GlobalSearchHotkey;

        // Tray
        s.Tray.CloseBehavior = CloseBehavior?.ToLowerInvariant() switch
        {
            "tray" => "tray",
            "exit" => "exit",
            _ => "ask",
        };
        s.Tray.MinimizeToTray = s.Tray.CloseBehavior == "tray";
        if (s.Tray.MinimizeToTray)
            ShowTrayIcon = true;
        s.Tray.StartWithWindows = StartWithWindows;
        s.Tray.ShowTrayIcon = ShowTrayIcon;
        s.Tray.DoubleClickAction = DoubleClickAction;
    }

    /// <summary>
    /// Gets the current configuration object. Used by the application layer.
    /// </summary>
    public AppConfig GetConfig() => _config;

    #endregion

    protected override void OnError(Exception ex, string errorContext)
    {
        base.OnError(ex, errorContext);
        StatusIsError = true;
        StatusMessage = L.F("错误：{0}", ex.Message);
    }
}
