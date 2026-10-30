using CipherVault.Core.Models;

namespace CipherVault.Core.Interfaces;

public interface IImportService
{
    string DetectFormat(string csvContent);
    int CountDataRows(string csvContent);
    List<ImportPreviewRow> PreviewImport(string csvContent, string format, int maxRows = 5);
    Task<ImportResult> ImportAsync(string csvContent, string format, Dictionary<string, string> fieldMapping, string? targetGroupId);
}

public sealed class ImportPreviewRow
{
    public int RowNumber { get; init; }
    public Dictionary<string, string> Fields { get; init; } = new();
}

public sealed class ImportResult
{
    public int SuccessCount { get; init; }
    public int SkippedCount { get; init; }
    public int FailedCount { get; init; }
    public List<string> Errors { get; init; } = new();
}
