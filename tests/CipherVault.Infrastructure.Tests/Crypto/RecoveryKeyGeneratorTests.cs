namespace CipherVault.Infrastructure.Tests.Crypto;

using CipherVault.Core.Models;
using CipherVault.Infrastructure.Crypto;
using FluentAssertions;
using Xunit;

public sealed class RecoveryKeyGeneratorTests
{
    [Fact]
    public void Generate_ProducesTwelveWordMnemonicWithValidChecksum()
    {
        var sut = new RecoveryKeyGenerator();

        RecoveryKey key = sut.Generate();

        key.Words.Should().HaveCount(12);
        sut.Validate(key).Should().BeTrue();
        key.Words.Should().OnlyContain(word => RecoveryKeyGenerator.DefaultEnglishWordList.Contains(word));
    }

    [Fact]
    public void Validate_AcceptsOfficialAllZeroEntropyVector()
    {
        var sut = new RecoveryKeyGenerator();
        var key = new RecoveryKey
        {
            Words = Enumerable.Repeat("abandon", 11).Append("about").ToArray(),
        };

        sut.Validate(key).Should().BeTrue();
    }

    [Fact]
    public void Validate_RejectsKnownChecksumMismatch()
    {
        var sut = new RecoveryKeyGenerator();
        var words = Enumerable.Repeat("abandon", 11).Append("ability").ToArray();

        sut.Validate(new RecoveryKey { Words = words }).Should().BeFalse();
    }
}
