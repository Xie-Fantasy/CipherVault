namespace CipherVault.Infrastructure.Security;

public sealed class BruteForceGuard
{
    private int _failedAttempts;
    private DateTime? _lockoutUntil;

    public int FailedAttempts => _failedAttempts;
    public bool IsLockedOut => _lockoutUntil.HasValue && DateTime.UtcNow < _lockoutUntil.Value;
    public TimeSpan? LockoutRemaining => IsLockedOut ? _lockoutUntil!.Value - DateTime.UtcNow : null;

    public int GetDelayMilliseconds()
    {
        if (IsLockedOut) return (int)LockoutRemaining!.Value.TotalMilliseconds;
        return _failedAttempts switch
        {
            < 3 => 0,
            < 6 => 2000,
            < 9 => 5000,
            < 11 => 15000,
            // Once a persisted lockout has expired, allow one new attempt. A failure
            // immediately starts a fresh five-minute lockout in RecordFailure().
            _ => 0
        };
    }

    public void RecordFailure()
    {
        if (_failedAttempts < int.MaxValue)
            _failedAttempts++;
        if (_failedAttempts >= 11)
        {
            _lockoutUntil = DateTime.UtcNow.AddMinutes(5);
        }
    }

    public void RecordSuccess()
    {
        _failedAttempts = 0;
        _lockoutUntil = null;
    }

    public void LoadState(uint failedAttempts, DateTime? lockoutUntil)
    {
        _failedAttempts = failedAttempts > int.MaxValue ? int.MaxValue : (int)failedAttempts;
        _lockoutUntil = lockoutUntil;
    }

    public (uint failedAttempts, DateTime? lockoutUntil) GetState()
        => ((uint)_failedAttempts, _lockoutUntil);
}
