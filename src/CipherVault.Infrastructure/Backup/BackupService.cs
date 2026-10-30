namespace CipherVault.Infrastructure.Backup;

using CipherVault.Core.Interfaces;

/// <summary>
/// Implements <see cref="IBackupService"/> for creating, restoring, and managing
/// encrypted .cvault file backups with a retention policy.
/// </summary>
public sealed class BackupService : IBackupService
{
    private string? _customBackupDirectory;
    private int _maxBackups = 100;

    public void Configure(string? backupDirectory, int maxBackups)
    {
        _customBackupDirectory = string.IsNullOrWhiteSpace(backupDirectory)
            ? null
            : Path.GetFullPath(backupDirectory);
        _maxBackups = Math.Clamp(maxBackups, 1, 10_000);
    }

    /// <inheritdoc />
    /// <summary>
    /// Creates a backup of the specified .cvault file in a "backups" subdirectory
    /// next to the vault file. Naming: {vault_name}_backup_{yyyyMMdd_HHmmss}.cvault
    /// </summary>
    public async Task CreateBackupAsync(string vaultFilePath)
    {
        ArgumentNullException.ThrowIfNull(vaultFilePath);

        if (!File.Exists(vaultFilePath))
            throw new FileNotFoundException("Vault file not found.", vaultFilePath);

        string backupDir = GetBackupDirectory(vaultFilePath);
        Directory.CreateDirectory(backupDir);

        string vaultName = Path.GetFileNameWithoutExtension(vaultFilePath);
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
        string backupFileName = $"{vaultName}_backup_{timestamp}.cvault";
        string backupPath = Path.Combine(backupDir, backupFileName);
        for (int suffix = 1; File.Exists(backupPath); suffix++)
        {
            backupFileName = $"{vaultName}_backup_{timestamp}_{suffix}.cvault";
            backupPath = Path.Combine(backupDir, backupFileName);
        }

        // Write to a temp file first, then atomically move to prevent partial backups
        string tempPath = backupPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await Task.Run(() =>
            {
                using var source = new FileStream(vaultFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var dest = new FileStream(tempPath, FileMode.Create, FileAccess.Write);
                source.CopyTo(dest);
            });

            // Atomic rename
            File.Move(tempPath, backupPath, overwrite: false);
            await CleanupOldBackupsAsync(vaultFilePath, _maxBackups);
        }
        catch
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
            throw;
        }
    }

    /// <inheritdoc />
    /// <summary>
    /// Lists all backup files for the given vault, sorted by timestamp descending (newest first).
    /// </summary>
    public Task<List<BackupInfo>> GetBackupsAsync(string vaultFilePath)
    {
        ArgumentNullException.ThrowIfNull(vaultFilePath);

        string backupDir = GetBackupDirectory(vaultFilePath);
        var backups = new List<BackupInfo>();

        if (!Directory.Exists(backupDir))
            return Task.FromResult(backups);

        string vaultName = Path.GetFileNameWithoutExtension(vaultFilePath);
        string pattern = $"{vaultName}_backup_*.cvault";

        var files = Directory.GetFiles(backupDir, pattern)
            .Select(path =>
            {
                var fileName = Path.GetFileNameWithoutExtension(path);
                // Extract timestamp from filename: {name}_backup_{yyyyMMdd_HHmmss}
                var timestampStr = ExtractTimestamp(fileName, vaultName);
                var fileInfo = new FileInfo(path);

                return new BackupInfo
                {
                    FilePath = path,
                    Timestamp = timestampStr ?? fileInfo.LastWriteTimeUtc,
                    FileSizeBytes = fileInfo.Length
                };
            })
            .OrderByDescending(b => b.Timestamp)
            .ToList();

        return Task.FromResult(files);
    }

    /// <inheritdoc />
    /// <summary>
    /// Restores a vault from a backup file by copying it to the target path.
    /// Creates a safety backup of the current state before restoring.
    /// </summary>
    public async Task RestoreFromBackupAsync(string backupFilePath, string targetVaultFilePath)
    {
        ArgumentNullException.ThrowIfNull(backupFilePath);
        ArgumentNullException.ThrowIfNull(targetVaultFilePath);

        if (!File.Exists(backupFilePath))
            throw new FileNotFoundException("Backup file not found.", backupFilePath);

        // Create a safety backup of the current state before restoring
        if (File.Exists(targetVaultFilePath))
        {
            await CreateBackupAsync(targetVaultFilePath);
        }

        // Copy backup to target, overwriting the current vault
        await Task.Run(() =>
        {
            string tempPath = targetVaultFilePath + ".restore.tmp";
            try
            {
                File.Copy(backupFilePath, tempPath, overwrite: true);
                File.Move(tempPath, targetVaultFilePath, overwrite: true);
            }
            catch
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
                throw;
            }
        });
    }

    /// <inheritdoc />
    /// <summary>
    /// Cleans up old backups according to the retention policy:
    /// - Last 24 hours: keep all
    /// - 24h-7d: keep 1 per hour
    /// - 7d-30d: keep 1 per day
    /// - 30d+: keep 1 per week
    /// Also respects the maximum backup count.
    /// </summary>
    public async Task CleanupOldBackupsAsync(string vaultFilePath, int maxBackups = 100)
    {
        ArgumentNullException.ThrowIfNull(vaultFilePath);

        var allBackups = await GetBackupsAsync(vaultFilePath);
        var now = DateTime.Now;
        var toDelete = new List<string>();

        // Group backups by retention bucket
        var last24h = new List<BackupInfo>();
        var hourlyBuckets = new Dictionary<string, List<BackupInfo>>();   // 24h-7d
        var dailyBuckets = new Dictionary<string, List<BackupInfo>>();    // 7d-30d
        var weeklyBuckets = new Dictionary<string, List<BackupInfo>>();   // 30d+

        foreach (var backup in allBackups)
        {
            var age = now - backup.Timestamp;

            if (age.TotalHours <= 24)
            {
                last24h.Add(backup);
            }
            else if (age.TotalDays <= 7)
            {
                // Bucket by year-day-hour
                var key = backup.Timestamp.ToString("yyyy-DDD-HH");
                if (!hourlyBuckets.TryGetValue(key, out var list))
                {
                    list = new List<BackupInfo>();
                    hourlyBuckets[key] = list;
                }
                list.Add(backup);
            }
            else if (age.TotalDays <= 30)
            {
                // Bucket by year-day
                var key = backup.Timestamp.ToString("yyyy-DDD");
                if (!dailyBuckets.TryGetValue(key, out var list))
                {
                    list = new List<BackupInfo>();
                    dailyBuckets[key] = list;
                }
                list.Add(backup);
            }
            else
            {
                // Bucket by ISO week
                var key = $"{backup.Timestamp.Year}-W{System.Globalization.CultureInfo.InvariantCulture.Calendar.GetWeekOfYear(backup.Timestamp, System.Globalization.CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday)}";
                if (!weeklyBuckets.TryGetValue(key, out var list))
                {
                    list = new List<BackupInfo>();
                    weeklyBuckets[key] = list;
                }
                list.Add(backup);
            }
        }

        // Keep all from last 24h
        // For hourly, daily, weekly buckets: keep only the newest in each bucket
        foreach (var bucket in hourlyBuckets.Values)
        {
            var sorted = bucket.OrderByDescending(b => b.Timestamp).ToList();
            toDelete.AddRange(sorted.Skip(1).Select(b => b.FilePath));
        }

        foreach (var bucket in dailyBuckets.Values)
        {
            var sorted = bucket.OrderByDescending(b => b.Timestamp).ToList();
            toDelete.AddRange(sorted.Skip(1).Select(b => b.FilePath));
        }

        foreach (var bucket in weeklyBuckets.Values)
        {
            var sorted = bucket.OrderByDescending(b => b.Timestamp).ToList();
            toDelete.AddRange(sorted.Skip(1).Select(b => b.FilePath));
        }

        // After retention policy, if still over maxBackups, delete oldest
        var remainingBackups = allBackups
            .Where(b => !toDelete.Contains(b.FilePath))
            .OrderByDescending(b => b.Timestamp)
            .ToList();

        if (remainingBackups.Count > maxBackups)
        {
            var excess = remainingBackups.Skip(maxBackups);
            toDelete.AddRange(excess.Select(b => b.FilePath));
        }

        // Delete files
        await Task.Run(() =>
        {
            foreach (var path in toDelete)
            {
                try
                {
                    if (File.Exists(path))
                        File.Delete(path);
                }
                catch
                {
                    // Best effort deletion
                }
            }
        });
    }

    #region Helpers

    private string GetBackupDirectory(string vaultFilePath)
    {
        if (!string.IsNullOrWhiteSpace(_customBackupDirectory))
            return _customBackupDirectory;

        string? dir = Path.GetDirectoryName(vaultFilePath);
        if (string.IsNullOrEmpty(dir))
            dir = ".";
        return Path.Combine(dir, "backups");
    }

    private static DateTime? ExtractTimestamp(string fileName, string vaultName)
    {
        // Expected format: {vaultName}_backup_{yyyyMMdd_HHmmss_fff}.
        // The legacy second-resolution format remains readable.
        string prefix = $"{vaultName}_backup_";
        if (!fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return null;

        string timestampStr = fileName[prefix.Length..];
        if (timestampStr.Length > 19 && timestampStr[19] == '_')
            timestampStr = timestampStr[..19];
        string[] formats = ["yyyyMMdd_HHmmss_fff", "yyyyMMdd_HHmmss"];
        if (DateTime.TryParseExact(timestampStr, formats,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out var timestamp))
            return timestamp;

        return null;
    }

    #endregion
}
