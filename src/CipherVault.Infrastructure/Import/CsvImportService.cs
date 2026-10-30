namespace CipherVault.Infrastructure.Import;

using System.Security.Cryptography;
using System.Text;
using CipherVault.Core.Interfaces;
using CipherVault.Core.Models;
using CipherVault.Infrastructure.Crypto;

/// <summary>
/// Implements <see cref="IImportService"/> for importing passwords from CSV files
/// exported by browsers (Chrome, Edge, Firefox) and KeePass.
/// Includes a hand-written CSV parser that handles quoted fields with embedded commas.
/// </summary>
public sealed class CsvImportService : IImportService
{
    private readonly IVaultRepository _repository;
    private readonly ICryptoService _cryptoService;
    private readonly VaultKeyManager _keyManager;

    public CsvImportService(
        IVaultRepository repository,
        ICryptoService cryptoService,
        VaultKeyManager keyManager)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _cryptoService = cryptoService ?? throw new ArgumentNullException(nameof(cryptoService));
        _keyManager = keyManager ?? throw new ArgumentNullException(nameof(keyManager));
    }

    /// <inheritdoc />
    public string DetectFormat(string csvContent)
    {
        ArgumentNullException.ThrowIfNull(csvContent);

        var firstLine = csvContent.Split('\n')[0].Trim().TrimEnd('\r');
        return BrowserFormatDetector.DetectFormat(firstLine);
    }

    /// <inheritdoc />
    public int CountDataRows(string csvContent)
    {
        ArgumentNullException.ThrowIfNull(csvContent);
        return Math.Max(0, ParseCsv(csvContent).Count - 1);
    }

    /// <inheritdoc />
    public List<ImportPreviewRow> PreviewImport(string csvContent, string format, int maxRows = 5)
    {
        ArgumentNullException.ThrowIfNull(csvContent);

        var rows = new List<ImportPreviewRow>();
        var parsed = ParseCsv(csvContent);

        if (parsed.Count < 2) // Need at least header + 1 data row
            return rows;

        var headers = parsed[0];
        if (headers.Count > 0)
            headers[0] = headers[0].TrimStart('\uFEFF');
        int rowCount = Math.Min(maxRows, parsed.Count - 1);

        for (int i = 0; i < rowCount; i++)
        {
            var dataRow = parsed[i + 1];
            var fields = new Dictionary<string, string>();

            for (int col = 0; col < headers.Count && col < dataRow.Count; col++)
            {
                fields[headers[col]] = dataRow[col];
            }

            rows.Add(new ImportPreviewRow
            {
                RowNumber = i + 1,
                Fields = fields
            });
        }

        return rows;
    }

    /// <inheritdoc />
    public async Task<ImportResult> ImportAsync(string csvContent, string format,
        Dictionary<string, string> fieldMapping, string? targetGroupId)
    {
        ArgumentNullException.ThrowIfNull(csvContent);
        ArgumentNullException.ThrowIfNull(fieldMapping);

        var result = new ImportResult();
        int successCount = 0;
        int skippedCount = 0;
        int failedCount = 0;
        var errors = new List<string>();

        var parsed = ParseCsv(csvContent);
        if (parsed.Count < 2)
        {
            errors.Add("CSV file has no data rows.");
            return new ImportResult
            {
                SuccessCount = 0,
                SkippedCount = 0,
                FailedCount = 0,
                Errors = errors
            };
        }

        var headers = parsed[0];
        if (headers.Count > 0)
            headers[0] = headers[0].TrimStart('\uFEFF');

        // Build reverse mapping: CSV column index → VaultEntry property name
        var columnIndexToProperty = new Dictionary<int, string>();
        for (int col = 0; col < headers.Count; col++)
        {
            if (fieldMapping.TryGetValue(headers[col], out var propertyName))
            {
                columnIndexToProperty[col] = propertyName;
            }
        }

        // Get existing entries for deduplication
        var existingEntries = await _repository.GetEntriesAsync();
        var existingKeys = new HashSet<string>(
            existingEntries.Select(e => BuildDedupeKey(e.Title, e.Username, e.Url)));

        // Import all accepted rows in one SQLite transaction. Individual malformed
        // rows are still reported and skipped, while successful rows commit together
        // and trigger only one encrypted-vault persistence notification.
        await _repository.ExecuteTransactionAsync(async () =>
        {
            for (int rowIdx = 1; rowIdx < parsed.Count; rowIdx++)
            {
                var dataRow = parsed[rowIdx];

                try
                {
                    var entry = new VaultEntry
                    {
                        CredentialType = CredentialType.Login,
                        GroupId = targetGroupId,
                    };

                    // Map CSV columns to entry properties
                    for (int col = 0; col < dataRow.Count && col < headers.Count; col++)
                    {
                        if (!columnIndexToProperty.TryGetValue(col, out var propertyName))
                            continue;

                        var value = dataRow[col];
                        if (string.IsNullOrWhiteSpace(value))
                            continue;

                        switch (propertyName)
                        {
                            case "Title":
                                entry.Title = value;
                                break;
                            case "Username":
                                entry.Username = value;
                                break;
                            case "Password":
                                entry.PasswordEncrypted = EncryptPassword(entry.Id, value);
                                break;
                            case "Url":
                                entry.Url = value;
                                break;
                            case "Notes":
                                entry.Notes = value;
                                break;
                        }
                    }

                    // Skip entries without a title
                    if (string.IsNullOrWhiteSpace(entry.Title))
                    {
                        errors.Add($"Row {rowIdx}: Skipped - no title.");
                        skippedCount++;
                        continue;
                    }

                    // Deduplication: check title + username + url
                    var dedupeKey = BuildDedupeKey(entry.Title, entry.Username, entry.Url);
                    if (existingKeys.Contains(dedupeKey))
                    {
                        skippedCount++;
                        continue;
                    }

                    await _repository.InsertEntryAsync(entry);
                    existingKeys.Add(dedupeKey);
                    successCount++;
                }
                catch (Exception ex)
                {
                    failedCount++;
                    errors.Add($"Row {rowIdx}: {ex.Message}");
                }
            }
        });

        return new ImportResult
        {
            SuccessCount = successCount,
            SkippedCount = skippedCount,
            FailedCount = failedCount,
            Errors = errors
        };
    }

    #region CSV Parser

    /// <summary>
    /// Parses CSV content into a list of rows, where each row is a list of field values.
    /// Handles quoted fields with embedded commas, double-quoted escape sequences,
    /// and CRLF/LF line endings.
    /// </summary>
    private static List<List<string>> ParseCsv(string csvContent)
    {
        var rows = new List<List<string>>();

        if (string.IsNullOrWhiteSpace(csvContent))
            return rows;

        int pos = 0;
        int length = csvContent.Length;

        while (pos < length)
        {
            var row = ParseRow(csvContent, ref pos);
            if (row.Count > 0 || pos < length)
            {
                // Only add non-empty rows, or empty rows that are followed by more data
                if (row.Count > 0)
                    rows.Add(row);
            }
        }

        return rows;
    }

    private static List<string> ParseRow(string content, ref int pos)
    {
        var fields = new List<string>();
        int length = content.Length;

        while (pos < length)
        {
            string field = ParseField(content, ref pos);
            fields.Add(field);

            if (pos >= length)
                break;

            char ch = content[pos];

            if (ch == ',')
            {
                pos++; // Skip comma, continue to next field
            }
            else if (ch == '\r')
            {
                pos++;
                if (pos < length && content[pos] == '\n')
                    pos++;
                break; // End of row
            }
            else if (ch == '\n')
            {
                pos++;
                break; // End of row
            }
            else
            {
                // Unexpected character, skip
                pos++;
            }
        }

        return fields;
    }

    private static string ParseField(string content, ref int pos)
    {
        int length = content.Length;

        if (pos >= length)
            return string.Empty;

        // Check if field is quoted
        if (content[pos] == '"')
        {
            return ParseQuotedField(content, ref pos);
        }

        // Unquoted field: read until comma, newline, or end
        var sb = new StringBuilder();
        while (pos < length)
        {
            char ch = content[pos];
            if (ch == ',' || ch == '\r' || ch == '\n')
                break;
            sb.Append(ch);
            pos++;
        }

        return sb.ToString();
    }

    private static string ParseQuotedField(string content, ref int pos)
    {
        int length = content.Length;
        pos++; // Skip opening quote

        var sb = new StringBuilder();
        while (pos < length)
        {
            char ch = content[pos];

            if (ch == '"')
            {
                // Check for escaped quote ("")
                if (pos + 1 < length && content[pos + 1] == '"')
                {
                    sb.Append('"');
                    pos += 2;
                }
                else
                {
                    pos++; // Skip closing quote
                    break;
                }
            }
            else
            {
                sb.Append(ch);
                pos++;
            }
        }

        return sb.ToString();
    }

    #endregion

    #region Helpers

    private byte[] EncryptPassword(string entryId, string password)
    {
        if (!_keyManager.IsUnlocked)
            throw new InvalidOperationException("The vault must be unlocked before importing passwords.");

        byte[] plaintext = Encoding.UTF8.GetBytes(password);
        try
        {
            var (ciphertext, nonce, tag) = _keyManager.UseKey(
                key => EntryPasswordCrypto.Encrypt(
                    _cryptoService, key, entryId, plaintext));

            var combined = new byte[nonce.Length + ciphertext.Length + tag.Length];
            Buffer.BlockCopy(nonce, 0, combined, 0, nonce.Length);
            Buffer.BlockCopy(ciphertext, 0, combined, nonce.Length, ciphertext.Length);
            Buffer.BlockCopy(tag, 0, combined, nonce.Length + ciphertext.Length, tag.Length);
            return combined;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static string BuildDedupeKey(string? title, string? username, string? url)
    {
        return $"{title?.Trim().ToLowerInvariant()}|{username?.Trim().ToLowerInvariant()}|{url?.Trim().ToLowerInvariant()}";
    }

    #endregion
}
