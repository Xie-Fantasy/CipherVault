using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CipherVault.Core.Config;
using CipherVault.Core.Interfaces;

namespace CipherVault.App.Config;

/// <summary>
/// Implementation of IConfigService that manages configuration in %LOCALAPPDATA%/CipherVault/config.json.
/// </summary>
public sealed class ConfigService : IConfigService
{
    private readonly string _configFilePath;
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private AppConfig? _cachedConfig;

    public ConfigService(string? configFilePath = null)
    {
        if (string.IsNullOrWhiteSpace(configFilePath))
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var configDir = Path.Combine(localAppData, "CipherVault");
            _configFilePath = Path.Combine(configDir, "config.json");
        }
        else
        {
            _configFilePath = Path.GetFullPath(configFilePath);
        }
    }

    public string ConfigFilePath => _configFilePath;

    public bool IsFirstRun
    {
        get
        {
            if (!File.Exists(_configFilePath))
                return true;

            var config = Load();
            return config.Vaults.Length == 0;
        }
    }

    public AppConfig Load()
    {
        if (_cachedConfig != null)
            return _cachedConfig;

        try
        {
            if (File.Exists(_configFilePath))
            {
                string json = File.ReadAllText(_configFilePath);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = JsonCommentHandling.Skip,
                };
                _cachedConfig = Normalize(JsonSerializer.Deserialize<AppConfig>(json, options) ?? new AppConfig());
            }
            else
            {
                _cachedConfig = new AppConfig();
            }
        }
        catch (Exception)
        {
            PreserveCorruptedConfig();
            _cachedConfig = new AppConfig();
        }

        return _cachedConfig;
    }

    public async Task SaveAsync(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        await _saveLock.WaitAsync().ConfigureAwait(false);
        string? temporaryPath = null;
        try
        {
            // Ensure directory exists
            var directory = Path.GetDirectoryName(_configFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
            };

            AppConfig normalized = Normalize(config);
            string json = JsonSerializer.Serialize(normalized, options);
            temporaryPath = _configFilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";

            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            await using (var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                await writer.WriteAsync(json).ConfigureAwait(false);
                await writer.FlushAsync().ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, _configFilePath, overwrite: true);
            temporaryPath = null;

            // Update cache
            _cachedConfig = normalized;
        }
        finally
        {
            if (temporaryPath != null)
            {
                try { File.Delete(temporaryPath); }
                catch { /* best-effort cleanup */ }
            }
            _saveLock.Release();
        }
    }

    public void AddVault(string name, string path)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path))
            return;

        var config = Load();

        // Check if vault already exists (match by path)
        if (config.Vaults.Any(v => v.Path.Equals(path, StringComparison.OrdinalIgnoreCase)))
            return;

        // Add new vault
        var vaults = config.Vaults.ToList();
        vaults.Add(new AppConfigVault { Name = name, Path = path });
        config.Vaults = vaults.ToArray();

        // Save synchronously for simplicity
        SaveAsync(config).GetAwaiter().GetResult();
    }

    public void RemoveVault(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        var config = Load();
        var vaults = config.Vaults.Where(v => !v.Path.Equals(path, StringComparison.OrdinalIgnoreCase)).ToArray();
        config.Vaults = vaults;

        // If the removed vault was the last opened, clear it
        if (config.LastOpenedVault?.Equals(path, StringComparison.OrdinalIgnoreCase) == true)
        {
            config.LastOpenedVault = vaults.Length > 0 ? vaults[0].Path : null;
        }

        SaveAsync(config).GetAwaiter().GetResult();
    }

    public string? GetLastOpenedVaultPath()
    {
        var config = Load();
        return config.LastOpenedVault;
    }

    public void SetLastOpenedVault(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        var config = Load();
        config.LastOpenedVault = path;
        SaveAsync(config).GetAwaiter().GetResult();
    }

    private static AppConfig Normalize(AppConfig config)
    {
        config.Vaults ??= Array.Empty<AppConfigVault>();
        config.Vaults = config.Vaults
            .Where(vault => vault != null && !string.IsNullOrWhiteSpace(vault.Path))
            .GroupBy(vault => vault.Path, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        config.Settings ??= new AppConfigSettings();
        config.Settings.Security ??= new SecuritySettings();
        config.Settings.Appearance ??= new AppearanceSettings();
        config.Settings.Backup ??= new BackupSettings();
        config.Settings.Hotkey ??= new HotkeySettings();
        config.Settings.Tray ??= new TraySettings();
        config.Settings.Import ??= new ImportSettings();

        SecuritySettings security = config.Settings.Security;
        security.IdleTimeoutMinutes = Math.Clamp(security.IdleTimeoutMinutes, 1, 60);
        security.ClipboardClearSeconds = Math.Clamp(security.ClipboardClearSeconds, 5, 60);
        config.Settings.Backup.MaxBackups = Math.Max(config.Settings.Backup.MaxBackups, 1);
        config.Settings.Appearance.FontSize =
            config.Settings.Appearance.FontSize?.ToLowerInvariant() switch
            {
                "small" => "small",
                "large" => "large",
                _ => "medium",
            };
        config.Settings.Appearance.Language =
            config.Settings.Appearance.Language.Equals("en-US", StringComparison.OrdinalIgnoreCase)
                ? "en-US"
                : "zh-CN";
        config.Settings.Appearance.SidebarWidth = Math.Clamp(config.Settings.Appearance.SidebarWidth, 160, 420);
        config.Settings.Appearance.ListWidth = Math.Clamp(config.Settings.Appearance.ListWidth, 240, 520);
        config.Settings.Tray.CloseBehavior =
            config.Settings.Tray.CloseBehavior?.ToLowerInvariant() switch
            {
                "tray" => "tray",
                "exit" => "exit",
                _ => "ask",
            };
        config.Settings.Tray.MinimizeToTray =
            config.Settings.Tray.CloseBehavior == "tray";
        return config;
    }

    private void PreserveCorruptedConfig()
    {
        try
        {
            if (!File.Exists(_configFilePath))
                return;

            string directory = Path.GetDirectoryName(_configFilePath)!;
            string backupPath = Path.Combine(
                directory,
                $"config.corrupt.{DateTime.UtcNow:yyyyMMddHHmmssfff}.json");
            File.Copy(_configFilePath, backupPath, overwrite: false);
        }
        catch
        {
            // A damaged config must not prevent opening vault files manually.
        }
    }
}
