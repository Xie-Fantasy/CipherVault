namespace CipherVault.Infrastructure.Tests;

using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using CipherVault.Core.Models;
using CipherVault.Infrastructure.Crypto;
using CipherVault.Infrastructure.Database;
using CipherVault.Infrastructure.Export;
using CipherVault.Infrastructure.Import;
using CipherVault.Infrastructure.Security;
using CipherVault.Infrastructure.Tests.TestSupport;
using CipherVault.Infrastructure.Vault;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

public sealed class ImportExportTests
{
    [Fact]
    public async Task CsvImportHandlesQuotedRows_EncryptsPasswordsAndCommitsOnce()
    {
        using var connection = OpenRepositoryConnection();
        int persistenceNotifications = 0;
        using var repository = new VaultRepository(connection, () =>
        {
            persistenceNotifications++;
            return Task.CompletedTask;
        });
        using var secureMemory = new SecureMemoryManager();
        using var keyManager = new VaultKeyManager(secureMemory);
        byte[] key = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
        byte[] decryptKey = key.ToArray();
        keyManager.StoreKey(key);
        var crypto = new CryptoService();
        var sut = new CsvImportService(repository, crypto, keyManager);
        string csv = "\uFEFFname,url,username,password,note\r\n"
            + "\"Example, Inc\",https://example.com,user,\"p,a\"\"ss\",\"line1\nline2\"\r\n"
            + "Second,https://second.example,user2,password2,note2";
        var mapping = new Dictionary<string, string>
        {
            ["name"] = "Title",
            ["url"] = "Url",
            ["username"] = "Username",
            ["password"] = "Password",
            ["note"] = "Notes",
        };

        var result = await sut.ImportAsync(csv, "Chrome", mapping, null);

        sut.CountDataRows(csv).Should().Be(2);
        result.SuccessCount.Should().Be(2);
        persistenceNotifications.Should().Be(1);
        List<VaultEntry> entries = await repository.GetEntriesAsync();
        entries.Should().HaveCount(2);
        VaultEntry imported = entries.Single(entry => entry.Title == "Example, Inc");
        imported.Title.Should().Be("Example, Inc");
        imported.Notes.Should().Be("line1\nline2");
        DecryptPassword(imported, crypto, decryptKey).Should().Be("p,a\"ss");
        CryptographicOperations.ZeroMemory(decryptKey);
    }

    [Fact]
    public async Task PlaintextExportsPreservePasswordAndCustomFieldsWithoutPartialFiles()
    {
        using var temp = new TemporaryDirectory();
        var crypto = new CryptoService();
        byte[] key = SHA256.HashData(Encoding.UTF8.GetBytes("export-test-key"));
        var entry = new VaultEntry
        {
            Title = "=Exact title",
            Username = "user@example.com",
            Url = "https://example.com",
            Notes = "line one, line two",
            CustomFields = [new CustomField { Key = "区域", Value = "华东" }],
            PasswordEncrypted = EncryptPassword("+exact,password", crypto, key),
        };
        var sut = new ExportService();
        string csvPath = temp.GetPath("export.csv");
        string xmlPath = temp.GetPath("export.xml");

        await sut.ExportCsvAsync(csvPath, [entry], crypto, key);
        await sut.ExportKeePassXmlAsync(xmlPath, [entry], [], crypto, key);

        string csv = await File.ReadAllTextAsync(csvPath);
        csv.Should().Contain("\"=Exact title\"");
        csv.Should().Contain("\"+exact,password\"");
        XDocument xml = XDocument.Load(xmlPath);
        var values = xml.Descendants("String").ToDictionary(
            element => element.Element("Key")!.Value,
            element => element.Element("Value")!.Value);
        values["Password"].Should().Be("+exact,password");
        values["Custom:区域"].Should().Be("华东");
        Directory.EnumerateFiles(temp.RootPath, "*.tmp").Should().BeEmpty();
        CryptographicOperations.ZeroMemory(key);
    }

    private static SqliteConnection OpenRepositoryConnection()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        SchemaMigrator.InitializeSchema(connection);
        return connection;
    }

    private static byte[] EncryptPassword(string password, CryptoService crypto, byte[] key)
    {
        byte[] plaintext = Encoding.UTF8.GetBytes(password);
        try
        {
            var (ciphertext, nonce, tag) = crypto.Encrypt(key, plaintext);
            return [.. nonce, .. ciphertext, .. tag];
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static string DecryptPassword(VaultEntry entry, CryptoService crypto, byte[] key)
    {
        byte[] combined = entry.PasswordEncrypted!;
        byte[] plaintext = EntryPasswordCrypto.Decrypt(
            crypto,
            key,
            entry.Id,
            combined.AsSpan(12, combined.Length - 28).ToArray(),
            combined.AsSpan(0, 12).ToArray(),
            combined.AsSpan(combined.Length - 16, 16).ToArray());
        try
        {
            return Encoding.UTF8.GetString(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }
}
