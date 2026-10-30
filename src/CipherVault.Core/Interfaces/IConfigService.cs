using System.Threading.Tasks;
using CipherVault.Core.Config;

namespace CipherVault.Core.Interfaces;

/// <summary>
/// Service for reading and writing application configuration.
/// Manages the config.json file in %LOCALAPPDATA%/CipherVault/.
/// </summary>
public interface IConfigService
{
    /// <summary>
    /// Loads the application configuration from disk.
    /// Returns a default configuration if the file doesn't exist.
    /// </summary>
    AppConfig Load();

    /// <summary>
    /// Saves the application configuration to disk.
    /// </summary>
    Task SaveAsync(AppConfig config);

    /// <summary>
    /// Gets the full path to the configuration file.
    /// </summary>
    string ConfigFilePath { get; }

    /// <summary>
    /// Indicates whether this is the first run of the application.
    /// True when the config file doesn't exist or the Vaults array is empty.
    /// </summary>
    bool IsFirstRun { get; }

    /// <summary>
    /// Adds a vault to the configuration if not already present (matched by path).
    /// </summary>
    void AddVault(string name, string path);

    /// <summary>
    /// Removes a vault from the configuration by path.
    /// </summary>
    void RemoveVault(string path);

    /// <summary>
    /// Gets the path of the last opened vault, or null if none.
    /// </summary>
    string? GetLastOpenedVaultPath();

    /// <summary>
    /// Sets the last opened vault path.
    /// </summary>
    void SetLastOpenedVault(string path);
}
