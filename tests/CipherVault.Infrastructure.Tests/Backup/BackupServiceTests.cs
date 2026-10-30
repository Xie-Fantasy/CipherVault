namespace CipherVault.Infrastructure.Tests.Backup;

using System.Globalization;
using CipherVault.Infrastructure.Backup;
using CipherVault.Infrastructure.Tests.TestSupport;
using FluentAssertions;
using Xunit;

public sealed class BackupServiceTests
{
    private readonly BackupService _sut = new();

    [Fact]
    public async Task CreateBackupAsync_CopiesVaultAndListsMetadata()
    {
        using var temp = new TemporaryDirectory();
        string vaultPath = temp.GetPath("personal.cvault");
        byte[] content = [0x43, 0x56, 0x41, 0x55, 0x4C, 0x54];
        await File.WriteAllBytesAsync(vaultPath, content);

        await _sut.CreateBackupAsync(vaultPath);
        var backups = await _sut.GetBackupsAsync(vaultPath);

        backups.Should().ContainSingle();
        File.Exists(backups[0].FilePath).Should().BeTrue();
        backups[0].FileSizeBytes.Should().Be(content.Length);
        (await File.ReadAllBytesAsync(backups[0].FilePath)).Should().Equal(content);
    }

    [Fact]
    public async Task RestoreFromBackupAsync_ReplacesTargetAndKeepsSafetyBackup()
    {
        using var temp = new TemporaryDirectory();
        string targetPath = temp.GetPath("personal.cvault");
        string sourceBackupPath = temp.GetPath("external-backup.cvault");
        byte[] originalContent = [1, 2, 3];
        byte[] restoredContent = [9, 8, 7, 6];
        await File.WriteAllBytesAsync(targetPath, originalContent);
        await File.WriteAllBytesAsync(sourceBackupPath, restoredContent);

        await _sut.RestoreFromBackupAsync(sourceBackupPath, targetPath);

        (await File.ReadAllBytesAsync(targetPath)).Should().Equal(restoredContent);
        var safetyBackups = await _sut.GetBackupsAsync(targetPath);
        safetyBackups.Should().ContainSingle();
        (await File.ReadAllBytesAsync(safetyBackups[0].FilePath)).Should().Equal(originalContent);
    }

    [Fact]
    public async Task CleanupOldBackupsAsync_WhenOverMaximum_KeepsNewestBackups()
    {
        using var temp = new TemporaryDirectory();
        string vaultPath = temp.GetPath("personal.cvault");
        await File.WriteAllTextAsync(vaultPath, "vault");
        string backupDirectory = temp.GetPath("backups");
        Directory.CreateDirectory(backupDirectory);

        DateTime firstTimestamp = DateTime.Now.AddHours(-2);
        var created = new List<(string Path, DateTime Timestamp)>();
        for (int i = 0; i < 5; i++)
        {
            DateTime timestamp = firstTimestamp.AddMinutes(i);
            string fileName = $"personal_backup_{timestamp.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture)}.cvault";
            string path = Path.Combine(backupDirectory, fileName);
            await File.WriteAllTextAsync(path, $"backup-{i}");
            created.Add((path, timestamp));
        }

        await _sut.CleanupOldBackupsAsync(vaultPath, maxBackups: 2);

        var remaining = await _sut.GetBackupsAsync(vaultPath);
        string[] expectedPaths = created
            .OrderByDescending(item => item.Timestamp)
            .Take(2)
            .Select(item => item.Path)
            .ToArray();

        remaining.Select(item => item.FilePath).Should().Equal(expectedPaths);
    }

    [Fact]
    public async Task ConfigureUsesCustomDirectoryAndAutomaticMaximum()
    {
        using var temp = new TemporaryDirectory();
        string vaultPath = temp.GetPath("personal.cvault");
        string customDirectory = temp.GetPath("custom-backups");
        await File.WriteAllTextAsync(vaultPath, "version-0");
        _sut.Configure(customDirectory, maxBackups: 2);

        for (int index = 1; index <= 3; index++)
        {
            await File.WriteAllTextAsync(vaultPath, $"version-{index}");
            await _sut.CreateBackupAsync(vaultPath);
        }

        var backups = await _sut.GetBackupsAsync(vaultPath);
        backups.Should().HaveCount(2);
        backups.Should().OnlyContain(item => Path.GetDirectoryName(item.FilePath) == customDirectory);
    }

    [Fact]
    public async Task SchedulerRaisesCompletionAfterDurableBackup()
    {
        using var temp = new TemporaryDirectory();
        string vaultPath = temp.GetPath("scheduled.cvault");
        await File.WriteAllTextAsync(vaultPath, "vault");
        using var scheduler = new BackupScheduler(_sut, debounceMs: 20);
        var completion = new TaskCompletionSource<(string Path, DateTime Time)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        scheduler.BackupCompleted += (path, time) => completion.TrySetResult((path, time));

        scheduler.ScheduleBackup(vaultPath);
        var result = await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));

        result.Path.Should().Be(vaultPath);
        result.Time.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
        (await _sut.GetBackupsAsync(vaultPath)).Should().ContainSingle();
    }
}
