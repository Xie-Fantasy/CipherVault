namespace CipherVault.Core.Interfaces;

using CipherVault.Core.Models;

/// <summary>
/// Provides functionality to export vault entries to various file formats.
/// Supports an internal key-bound JSON archive plus plaintext CSV and KeePass-compatible XML.
/// </summary>
public interface IExportService
{
    /// <summary>
    /// Exports vault entries to a key-bound encrypted JSON archive.
    /// This low-level format is not presented as a standalone backup because restoring it
    /// requires the same live vault key; use a .cvault backup for recoverable encrypted copies.
    /// </summary>
    /// <param name="filePath">The destination file path.</param>
    /// <param name="entries">The vault entries to export.</param>
    /// <param name="groups">The vault groups to export.</param>
    /// <param name="tags">The vault tags to export.</param>
    /// <param name="encryptionKey">The 256-bit encryption key (32 bytes).</param>
    Task ExportEncryptedJsonAsync(string filePath, List<VaultEntry> entries, List<VaultGroup> groups, List<Tag> tags, byte[] encryptionKey);

    /// <summary>
    /// Exports vault entries to a CSV file (unencrypted, passwords in plaintext).
    /// User must be warned about security implications before calling this method.
    /// </summary>
    /// <param name="filePath">The destination file path.</param>
    /// <param name="entries">The vault entries to export.</param>
    /// <param name="cryptoService">The crypto service used to decrypt password fields.</param>
    /// <param name="vaultKey">The vault key used to decrypt per-entry passwords.</param>
    Task ExportCsvAsync(string filePath, List<VaultEntry> entries, ICryptoService cryptoService, byte[] vaultKey);

    /// <summary>
    /// Exports vault entries to a KeePass 2.x compatible plaintext XML format.
    /// User must be warned that passwords are decrypted into the destination file.
    /// </summary>
    /// <param name="filePath">The destination file path.</param>
    /// <param name="entries">The vault entries to export.</param>
    /// <param name="groups">The vault groups to export.</param>
    Task ExportKeePassXmlAsync(
        string filePath,
        List<VaultEntry> entries,
        List<VaultGroup> groups,
        ICryptoService cryptoService,
        byte[] vaultKey);

    /// <summary>
    /// Gets the file dialog filter string for the given export format.
    /// </summary>
    /// <param name="format">The export format.</param>
    /// <returns>A filter string suitable for SaveFileDialog.</returns>
    static string GetFileFilter(ExportFormat format) => format switch
    {
        ExportFormat.EncryptedJson => "加密备份文件 (*.cvault-export)|*.cvault-export",
        ExportFormat.Csv => "CSV 文件 (*.csv)|*.csv",
        ExportFormat.KeePassXml => "KeePass XML (*.xml)|*.xml",
        _ => "所有文件 (*.*)|*.*"
    };
}

/// <summary>
/// Supported export file formats.
/// </summary>
public enum ExportFormat
{
    /// <summary>Internal encrypted JSON archive bound to the current vault key.</summary>
    EncryptedJson,

    /// <summary>CSV file with plaintext passwords. Use with caution.</summary>
    Csv,

    /// <summary>KeePass 2.x compatible XML format.</summary>
    KeePassXml
}
