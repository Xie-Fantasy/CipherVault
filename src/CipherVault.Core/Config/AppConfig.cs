using System;
using System.Text.Json.Serialization;

namespace CipherVault.Core.Config;

/// <summary>
/// Root application configuration model matching the config.json schema.
/// </summary>
public sealed class AppConfig
{
    [JsonPropertyName("vaults")]
    public AppConfigVault[] Vaults { get; set; } = Array.Empty<AppConfigVault>();

    [JsonPropertyName("lastOpenedVault")]
    public string? LastOpenedVault { get; set; }

    [JsonPropertyName("settings")]
    public AppConfigSettings Settings { get; set; } = new();
}

/// <summary>
/// Represents a vault entry in the configuration file.
/// </summary>
public sealed class AppConfigVault
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Container for all application settings categories.
/// </summary>
public sealed class AppConfigSettings
{
    [JsonPropertyName("security")]
    public SecuritySettings Security { get; set; } = new();

    [JsonPropertyName("appearance")]
    public AppearanceSettings Appearance { get; set; } = new();

    [JsonPropertyName("backup")]
    public BackupSettings Backup { get; set; } = new();

    [JsonPropertyName("hotkey")]
    public HotkeySettings Hotkey { get; set; } = new();

    [JsonPropertyName("tray")]
    public TraySettings Tray { get; set; } = new();

    [JsonPropertyName("import")]
    public ImportSettings Import { get; set; } = new();
}

/// <summary>
/// Security-related settings including auto-lock behavior and clipboard protection.
/// </summary>
public sealed class SecuritySettings
{
    [JsonPropertyName("idleTimeoutMinutes")]
    public int IdleTimeoutMinutes { get; set; } = 5;

    [JsonPropertyName("lockOnSystemLock")]
    public bool LockOnSystemLock { get; set; } = true;

    [JsonPropertyName("lockOnWindowBlur")]
    public bool LockOnWindowBlur { get; set; }

    [JsonPropertyName("lockOnMinimize")]
    public bool LockOnMinimize { get; set; }

    [JsonPropertyName("clipboardClearSeconds")]
    public int ClipboardClearSeconds { get; set; } = 15;

    [JsonPropertyName("antiCapture")]
    public bool AntiCapture { get; set; }

    [JsonPropertyName("windowsHelloEnabled")]
    public bool WindowsHelloEnabled { get; set; }
}

/// <summary>
/// Appearance settings including theme and font size.
/// </summary>
public sealed class AppearanceSettings
{
    [JsonPropertyName("theme")]
    public string Theme { get; set; } = "system";

    [JsonPropertyName("fontSize")]
    public string FontSize { get; set; } = "medium";

    [JsonPropertyName("language")]
    public string Language { get; set; } = "zh-CN";

    [JsonPropertyName("sidebarWidth")]
    public int SidebarWidth { get; set; } = 220;

    [JsonPropertyName("listWidth")]
    public int ListWidth { get; set; } = 320;
}

/// <summary>
/// Backup settings including auto-backup and retention policy.
/// </summary>
public sealed class BackupSettings
{
    [JsonPropertyName("autoBackup")]
    public bool AutoBackup { get; set; } = true;

    [JsonPropertyName("backupDirectory")]
    public string BackupDirectory { get; set; } = string.Empty;

    [JsonPropertyName("maxBackups")]
    public int MaxBackups { get; set; } = 100;

    [JsonPropertyName("retentionPolicy")]
    public string RetentionPolicy { get; set; } = "standard";
}

/// <summary>
/// Global hotkey settings for quick access.
/// </summary>
public sealed class HotkeySettings
{
    [JsonPropertyName("globalSearch")]
    public string GlobalSearch { get; set; } = "Ctrl+Shift+P";
}

/// <summary>
/// System tray behavior settings.
/// </summary>
public sealed class TraySettings
{
    /// <summary>
    /// Action taken when the main-window close button is clicked:
    /// "ask", "tray", or "exit".
    /// </summary>
    [JsonPropertyName("closeBehavior")]
    public string CloseBehavior { get; set; } = "ask";

    [JsonPropertyName("minimizeToTray")]
    public bool MinimizeToTray { get; set; } = true;

    [JsonPropertyName("startWithWindows")]
    public bool StartWithWindows { get; set; }

    [JsonPropertyName("showTrayIcon")]
    public bool ShowTrayIcon { get; set; } = true;

    [JsonPropertyName("doubleClickAction")]
    public string DoubleClickAction { get; set; } = "openAndUnlock";
}

/// <summary>
/// Import-related settings.
/// </summary>
public sealed class ImportSettings
{
    [JsonPropertyName("defaultTargetGroup")]
    public string? DefaultTargetGroup { get; set; }
}
