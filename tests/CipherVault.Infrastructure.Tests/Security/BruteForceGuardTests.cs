namespace CipherVault.Infrastructure.Tests.Security;

using CipherVault.Infrastructure.Security;
using FluentAssertions;
using Xunit;

public sealed class BruteForceGuardTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(2, 0)]
    [InlineData(3, 2_000)]
    [InlineData(5, 2_000)]
    [InlineData(6, 5_000)]
    [InlineData(8, 5_000)]
    [InlineData(9, 15_000)]
    [InlineData(10, 15_000)]
    public void GetDelayMilliseconds_UsesConfiguredThresholds(int failureCount, int expectedDelay)
    {
        var sut = new BruteForceGuard();
        RecordFailures(sut, failureCount);

        sut.IsLockedOut.Should().BeFalse();
        sut.GetDelayMilliseconds().Should().Be(expectedDelay);
    }

    [Fact]
    public void RecordFailure_OnEleventhFailure_StartsFiveMinuteLockout()
    {
        var sut = new BruteForceGuard();

        RecordFailures(sut, 11);

        sut.FailedAttempts.Should().Be(11);
        sut.IsLockedOut.Should().BeTrue();
        sut.LockoutRemaining.Should().NotBeNull();
        sut.LockoutRemaining!.Value.Should().BeGreaterThan(TimeSpan.FromMinutes(4));
        sut.GetDelayMilliseconds().Should().BeInRange(1, 300_000);
    }

    [Fact]
    public void RecordSuccess_AfterFailures_ClearsAttemptsAndLockout()
    {
        var sut = new BruteForceGuard();
        RecordFailures(sut, 11);

        sut.RecordSuccess();

        sut.FailedAttempts.Should().Be(0);
        sut.IsLockedOut.Should().BeFalse();
        sut.LockoutRemaining.Should().BeNull();
        sut.GetDelayMilliseconds().Should().Be(0);
    }

    private static void RecordFailures(BruteForceGuard guard, int count)
    {
        for (int i = 0; i < count; i++)
        {
            guard.RecordFailure();
        }
    }
}
