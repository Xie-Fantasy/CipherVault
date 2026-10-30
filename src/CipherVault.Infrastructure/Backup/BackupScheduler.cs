namespace CipherVault.Infrastructure.Backup;

using CipherVault.Core.Interfaces;

/// <summary>
/// Schedules automatic backups after vault modifications with a debounce mechanism.
/// When multiple changes occur in quick succession, only one backup is triggered
/// after the debounce period expires.
/// </summary>
public sealed class BackupScheduler : IDisposable
{
    private readonly IBackupService _backupService;
    private readonly int _debounceMs;
    private CancellationTokenSource? _debounceCts;
    private readonly object _lock = new();
    private bool _disposed;

    /// <summary>Raised after an automatic backup has been durably created.</summary>
    public event Action<string, DateTime>? BackupCompleted;

    /// <summary>
    /// Initializes a new instance of <see cref="BackupScheduler"/>.
    /// </summary>
    /// <param name="backupService">The backup service to use for creating backups.</param>
    /// <param name="debounceMs">The debounce delay in milliseconds. Default is 3000 (3 seconds).</param>
    public BackupScheduler(IBackupService backupService, int debounceMs = 3000)
    {
        _backupService = backupService ?? throw new ArgumentNullException(nameof(backupService));
        _debounceMs = debounceMs > 0 ? debounceMs : throw new ArgumentOutOfRangeException(nameof(debounceMs));
    }

    /// <summary>
    /// Schedules a backup for the specified vault file.
    /// If a backup is already scheduled, the timer is reset (debounced).
    /// </summary>
    /// <param name="vaultFilePath">The path to the .cvault file to back up.</param>
    public void ScheduleBackup(string vaultFilePath)
    {
        ArgumentNullException.ThrowIfNull(vaultFilePath);

        if (_disposed)
            throw new ObjectDisposedException(nameof(BackupScheduler));

        lock (_lock)
        {
            // Cancel any pending backup
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();

            _debounceCts = new CancellationTokenSource();
            var token = _debounceCts.Token;

            // Schedule a new backup after the debounce period
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(_debounceMs, token);

                    if (!token.IsCancellationRequested)
                    {
                        await _backupService.CreateBackupAsync(vaultFilePath);
                        BackupCompleted?.Invoke(vaultFilePath, DateTime.UtcNow);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Debounce cancelled, ignore
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.TraceError(
                        "Scheduled vault backup failed for {0}: {1}", vaultFilePath, ex);
                }
            }, token);
        }
    }

    /// <summary>
    /// Disposes the scheduler and cancels any pending backup.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        lock (_lock)
        {
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = null;
        }
    }
}
