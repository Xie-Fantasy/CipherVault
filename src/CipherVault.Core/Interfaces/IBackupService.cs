namespace CipherVault.Core.Interfaces;

public interface IBackupService
{
    void Configure(string? backupDirectory, int maxBackups);
    Task CreateBackupAsync(string vaultFilePath);
    Task<List<BackupInfo>> GetBackupsAsync(string vaultFilePath);
    Task RestoreFromBackupAsync(string backupFilePath, string targetVaultFilePath);
    Task CleanupOldBackupsAsync(string vaultFilePath, int maxBackups = 100);
}

public sealed class BackupInfo
{
    public string FilePath { get; init; } = string.Empty;
    public DateTime Timestamp { get; init; }
    public long FileSizeBytes { get; init; }
}
