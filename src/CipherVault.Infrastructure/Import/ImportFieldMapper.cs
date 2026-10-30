namespace CipherVault.Infrastructure.Import;

/// <summary>
/// Provides default field mappings from CSV columns to VaultEntry properties
/// for each supported import format.
/// </summary>
public static class ImportFieldMapper
{
    /// <summary>
    /// Returns the default field mapping for the specified format.
    /// Keys are CSV column names; values are VaultEntry property names.
    /// </summary>
    /// <param name="format">The detected or user-specified format (e.g., "Chrome", "Firefox", "KeePass").</param>
    /// <returns>A dictionary mapping CSV columns to VaultEntry properties.</returns>
    public static Dictionary<string, string> GetDefaultMapping(string format)
    {
        return format switch
        {
            "Chrome" or "Edge" => new()
            {
                ["name"] = "Title",
                ["url"] = "Url",
                ["username"] = "Username",
                ["password"] = "Password",
            },
            "Firefox" => new()
            {
                ["url"] = "Url",
                ["username"] = "Username",
                ["password"] = "Password",
            },
            "KeePass" => new()
            {
                ["Title"] = "Title",
                ["Username"] = "Username",
                ["Password"] = "Password",
                ["URL"] = "Url",
                ["Notes"] = "Notes",
            },
            _ => new()
        };
    }
}
