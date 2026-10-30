namespace CipherVault.Infrastructure.Export;

using System.Globalization;
using System.Text;
using System.Xml;
using CipherVault.Core.Interfaces;
using CipherVault.Core.Models;
using CipherVault.Infrastructure.Crypto;

/// <summary>
/// Implements <see cref="IExportService"/> to export vault data in encrypted JSON,
/// plaintext CSV, and KeePass 2.x XML formats.
/// </summary>
public sealed class ExportService : IExportService
{
    private const int FormatVersion = 1;

    /// <inheritdoc />
    public async Task ExportEncryptedJsonAsync(
        string filePath,
        List<VaultEntry> entries,
        List<VaultGroup> groups,
        List<Tag> tags,
        byte[] encryptionKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(tags);
        ArgumentNullException.ThrowIfNull(encryptionKey);

        // Build the export payload
        var payload = new ExportPayload
        {
            FormatVersion = FormatVersion,
            ExportedAt = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            Entries = entries.Select(MapEntryToDto).ToList(),
            Groups = groups.Select(g => new GroupDto
            {
                Id = g.Id,
                Name = g.Name,
                ParentId = g.ParentId,
                SortOrder = g.SortOrder,
                IconName = g.IconName
            }).ToList(),
            Tags = tags.Select(t => new TagDto
            {
                Id = t.Id,
                Name = t.Name,
                Color = t.Color
            }).ToList()
        };

        // Serialize to JSON bytes
        byte[] jsonBytes = Encoding.UTF8.GetBytes(
            System.Text.Json.JsonSerializer.Serialize(payload, JsonExportOptions.Default));

        // Encrypt using ICryptoService (AES-256-GCM)
        var crypto = new CipherVault.Infrastructure.Crypto.CryptoService();
        var (ciphertext, nonce, tag) = crypto.Encrypt(encryptionKey, jsonBytes);

        // Write file: Header(4B magic + 4B version) + Nonce(12B) + Tag(16B) + Ciphertext
        using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new BinaryWriter(fs, Encoding.UTF8, leaveOpen: false);

        // Magic bytes: "CVEX" (CipherVault Export)
        writer.Write(new byte[] { 0x43, 0x56, 0x45, 0x58 });
        // Format version (uint32 LE)
        writer.Write((uint)FormatVersion);
        // Nonce (12 bytes)
        writer.Write(nonce);
        // Tag (16 bytes)
        writer.Write(tag);
        // Ciphertext length (uint64 LE) for streaming reads
        writer.Write((ulong)ciphertext.Length);
        // Ciphertext
        writer.Write(ciphertext);

        // Zero the plaintext JSON bytes
        Array.Clear(jsonBytes);

        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task ExportCsvAsync(
        string filePath,
        List<VaultEntry> entries,
        ICryptoService cryptoService,
        byte[] vaultKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(cryptoService);
        ArgumentNullException.ThrowIfNull(vaultKey);

        string temporaryPath = CreateTemporaryPath(filePath);
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            await using (var writer = new StreamWriter(
                stream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)))
            {
                await writer.WriteLineAsync("Title,Type,Username,Password,URL,Notes,Tags,IsFavorite");

                foreach (var entry in entries)
                {
                    string password = DecryptPasswordField(entry, cryptoService, vaultKey);
                    try
                    {
                        string[] fields =
                        [
                            entry.Title,
                            entry.CredentialType.ToString(),
                            entry.Username ?? string.Empty,
                            password,
                            entry.Url ?? string.Empty,
                            entry.Notes ?? string.Empty,
                            string.Join("; ", entry.TagIds),
                        ];

                        await writer.WriteAsync(string.Join(',', fields.Select(QuoteCsvField)));
                        await writer.WriteAsync(entry.IsFavorite ? ",true" : ",false");
                        await writer.WriteLineAsync();
                    }
                    finally
                    {
                        // Managed strings cannot be zeroed reliably; release the shortest-lived
                        // reference immediately and never accumulate passwords in one buffer.
                        password = string.Empty;
                    }
                }

                await writer.FlushAsync();
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, filePath, overwrite: true);
        }
        catch
        {
            TryDelete(temporaryPath);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task ExportKeePassXmlAsync(
        string filePath,
        List<VaultEntry> entries,
        List<VaultGroup> groups,
        ICryptoService cryptoService,
        byte[] vaultKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(cryptoService);
        ArgumentNullException.ThrowIfNull(vaultKey);

        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "\t",
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
            Async = true
        };

        string temporaryPath = CreateTemporaryPath(filePath);
        try
        {
        await using var stream = new FileStream(
            temporaryPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            4096,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        using var writer = XmlWriter.Create(stream, settings);

        writer.WriteStartDocument();
        writer.WriteStartElement("KeePassFile");

        // Meta section
        writer.WriteStartElement("Meta");
        writer.WriteElementString("Generator", "CipherVault");
        writer.WriteElementString("DatabaseName", "CipherVault Export");
        writer.WriteElementString("DatabaseDescription", $"Exported from CipherVault on {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        writer.WriteEndElement(); // Meta

        // Root group
        writer.WriteStartElement("Root");
        writer.WriteStartElement("Group");
        writer.WriteElementString("UUID", Guid.NewGuid().ToString("N"));
        writer.WriteElementString("Name", "Root");

        // Build group lookup
        var groupDict = groups.ToDictionary(g => g.Id);
        var entriesByGroup = entries.GroupBy(e => e.GroupId ?? string.Empty).ToDictionary(g => g.Key, g => g.ToList());

        // Write groups recursively
        var rootGroups = groups.Where(g => g.ParentId == null).OrderBy(g => g.SortOrder).ToList();
        foreach (var group in rootGroups)
        {
            await WriteKeePassGroupAsync(writer, group, groups, entriesByGroup, cryptoService, vaultKey);
        }

        // Write entries that have no group (root-level entries)
        if (entriesByGroup.TryGetValue(string.Empty, out var rootEntries))
        {
            foreach (var entry in rootEntries)
            {
                WriteKeePassEntry(writer, entry, cryptoService, vaultKey);
            }
        }

        writer.WriteEndElement(); // Group (Root)
        writer.WriteEndElement(); // Root
        writer.WriteEndElement(); // KeePassFile
        writer.WriteEndDocument();

        await writer.FlushAsync();
        stream.Flush(flushToDisk: true);
        writer.Close();
        await stream.DisposeAsync();
        File.Move(temporaryPath, Path.GetFullPath(filePath), overwrite: true);
        }
        catch
        {
            TryDelete(temporaryPath);
            throw;
        }
    }

    #region Private Helpers

    private static async Task WriteKeePassGroupAsync(
        XmlWriter writer,
        VaultGroup group,
        List<VaultGroup> allGroups,
        Dictionary<string, List<VaultEntry>> entriesByGroup,
        ICryptoService cryptoService,
        byte[] vaultKey)
    {
        writer.WriteStartElement("Group");
        writer.WriteElementString("UUID", Guid.NewGuid().ToString("N"));
        writer.WriteElementString("Name", group.Name);

        // Write child groups
        var children = allGroups
            .Where(g => g.ParentId == group.Id)
            .OrderBy(g => g.SortOrder)
            .ToList();

        foreach (var child in children)
        {
            await WriteKeePassGroupAsync(writer, child, allGroups, entriesByGroup, cryptoService, vaultKey);
        }

        // Write entries in this group
        if (entriesByGroup.TryGetValue(group.Id, out var groupEntries))
        {
            foreach (var entry in groupEntries)
            {
                WriteKeePassEntry(writer, entry, cryptoService, vaultKey);
            }
        }

        writer.WriteEndElement(); // Group
    }

    private static void WriteKeePassEntry(
        XmlWriter writer,
        VaultEntry entry,
        ICryptoService cryptoService,
        byte[] vaultKey)
    {
        writer.WriteStartElement("Entry");
        writer.WriteElementString("UUID", entry.Id);

        // String fields
        WriteKeePassString(writer, "Title", entry.Title);
        WriteKeePassString(writer, "UserName", entry.Username ?? string.Empty);
        string password = DecryptPasswordField(entry, cryptoService, vaultKey);
        try
        {
            WriteKeePassString(writer, "Password", password);
        }
        finally
        {
            password = string.Empty;
        }
        WriteKeePassString(writer, "URL", entry.Url ?? string.Empty);
        WriteKeePassString(writer, "Notes", entry.Notes ?? string.Empty);

        // Type as a custom string field
        WriteKeePassString(writer, "Type", entry.CredentialType.ToString());
        foreach (CustomField field in entry.CustomFields)
        {
            WriteKeePassString(writer, $"Custom:{field.Key}", field.Value);
        }

        // Timestamps
        writer.WriteStartElement("Times");
        writer.WriteElementString("CreationTime", entry.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
        writer.WriteElementString("LastModificationTime", entry.ModifiedAt.ToString("O", CultureInfo.InvariantCulture));
        if (entry.LastAccessedAt.HasValue)
        {
            writer.WriteElementString("LastAccessTime", entry.LastAccessedAt.Value.ToString("O", CultureInfo.InvariantCulture));
        }
        writer.WriteEndElement(); // Times

        writer.WriteEndElement(); // Entry
    }

    private static void WriteKeePassString(XmlWriter writer, string key, string value, bool protect = false)
    {
        writer.WriteStartElement("String");
        writer.WriteElementString("Key", key);
        writer.WriteStartElement("Value");
        if (protect)
        {
            writer.WriteAttributeString("Protected", "True");
        }
        writer.WriteString(value);
        writer.WriteEndElement(); // Value
        writer.WriteEndElement(); // String
    }

    private static string DecryptPasswordField(VaultEntry entry, ICryptoService cryptoService, byte[] vaultKey)
    {
        if (entry.PasswordEncrypted == null)
            return string.Empty;

        try
        {
            byte[] combined = entry.PasswordEncrypted;

            // Format: Nonce(12B) + Ciphertext + Tag(16B)
            if (combined.Length < 28)
                return string.Empty;

            byte[] nonce = new byte[12];
            byte[] tag = new byte[16];
            byte[] ciphertext = new byte[combined.Length - 28];

            Buffer.BlockCopy(combined, 0, nonce, 0, 12);
            Buffer.BlockCopy(combined, 12, ciphertext, 0, ciphertext.Length);
            Buffer.BlockCopy(combined, combined.Length - 16, tag, 0, 16);

            byte[] plaintext = EntryPasswordCrypto.Decrypt(
                cryptoService, vaultKey, entry.Id, ciphertext, nonce, tag);
            string result = Encoding.UTF8.GetString(plaintext);
            Array.Clear(plaintext);

            return result;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string QuoteCsvField(string field)
    {
        // Preserve values byte-for-byte for migration fidelity. Consumers must not
        // open an untrusted CSV directly in formula-capable spreadsheet software.
        return $"\"{field.Replace("\"", "\"\"")}\"";
    }

    private static string CreateTemporaryPath(string destinationPath)
    {
        string fullPath = Path.GetFullPath(destinationPath);
        string? directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            throw new DirectoryNotFoundException("导出目录不存在。");
        return Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch { /* best-effort cleanup */ }
    }

    private static EntryDto MapEntryToDto(VaultEntry entry)
    {
        return new EntryDto
        {
            Id = entry.Id,
            Title = entry.Title,
            CredentialType = entry.CredentialType.ToString(),
            Username = entry.Username,
            PasswordEncrypted = entry.PasswordEncrypted != null
                ? Convert.ToBase64String(entry.PasswordEncrypted)
                : null,
            Url = entry.Url,
            Notes = entry.Notes,
            IsFavorite = entry.IsFavorite,
            GroupId = entry.GroupId,
            CustomFields = entry.CustomFields.Select(f => new CustomFieldDto
            {
                Key = f.Key,
                Value = f.Value,
                IsSensitive = f.IsSensitive
            }).ToList(),
            TagIds = entry.TagIds,
            CreatedAt = entry.CreatedAt.ToString("O", CultureInfo.InvariantCulture),
            ModifiedAt = entry.ModifiedAt.ToString("O", CultureInfo.InvariantCulture),
            LastAccessedAt = entry.LastAccessedAt?.ToString("O", CultureInfo.InvariantCulture)
        };
    }

    #endregion

    #region DTOs (internal, for JSON serialization)

    private sealed class ExportPayload
    {
        public int FormatVersion { get; set; }
        public string ExportedAt { get; set; } = string.Empty;
        public List<EntryDto> Entries { get; set; } = new();
        public List<GroupDto> Groups { get; set; } = new();
        public List<TagDto> Tags { get; set; } = new();
    }

    private sealed class EntryDto
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string CredentialType { get; set; } = string.Empty;
        public string? Username { get; set; }
        public string? PasswordEncrypted { get; set; }
        public string? Url { get; set; }
        public string? Notes { get; set; }
        public bool IsFavorite { get; set; }
        public string? GroupId { get; set; }
        public List<CustomFieldDto> CustomFields { get; set; } = new();
        public List<string> TagIds { get; set; } = new();
        public string CreatedAt { get; set; } = string.Empty;
        public string ModifiedAt { get; set; } = string.Empty;
        public string? LastAccessedAt { get; set; }
    }

    private sealed class GroupDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? ParentId { get; set; }
        public int SortOrder { get; set; }
        public string? IconName { get; set; }
    }

    private sealed class TagDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Color { get; set; }
    }

    private sealed class CustomFieldDto
    {
        public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public bool IsSensitive { get; set; }
    }

    #endregion

    #region JSON Options

    private static class JsonExportOptions
    {
        public static readonly System.Text.Json.JsonSerializerOptions Default = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
    }

    #endregion
}
