namespace CipherVault.UI.Tests.ViewModels;

using System.IO;
using CipherVault.Core.Security;
using CipherVault.Infrastructure.Crypto;
using CipherVault.Infrastructure.Vault;
using CipherVault.UI.ViewModels;
using FluentAssertions;
using Xunit;

public sealed class SetupWizardViewModelTests
{
    [Fact]
    public void NewWizard_ProvidesEditableDefaultVaultLocation()
    {
        var sut = CreateViewModel();
        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string expectedDefault = Path.Combine(
            documents,
            "CipherVault",
            "个人保险库.cvault");

        sut.VaultName.Should().Be("个人保险库");
        sut.VaultFilePath.Should().Be(expectedDefault);

        sut.VaultName = "工作账号";
        sut.VaultFilePath.Should().Be(Path.Combine(
            documents,
            "CipherVault",
            "工作账号.cvault"));

        string customPath = Path.Combine(documents, "自定义位置.cvault");
        sut.UseSelectedVaultFilePath(customPath);
        sut.VaultName = "名称再次变化";

        sut.VaultFilePath.Should().Be(customPath);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("12345", false)]
    [InlineData("123456", true)]
    [InlineData("123456789012", true)]
    [InlineData("1234567890123", true)]
    public void MasterPasswordPolicy_RequiresSixToOneHundredTwentyEightCharacters(
        string password,
        bool expected)
    {
        MasterPasswordPolicy.IsValid(password).Should().Be(expected);
    }

    [Fact]
    public void BrowseLocationCommand_RequestsNativePicker()
    {
        var sut = CreateViewModel();
        bool requested = false;
        sut.BrowseLocationRequested += (_, _) => requested = true;

        sut.BrowseLocationCommand.Execute(null);

        requested.Should().BeTrue();
    }

    [Fact]
    public void PasswordStep_AcceptsSixToOneHundredTwentyEightMatchingCharacters()
    {
        var sut = CreateViewModel();
        sut.CurrentStep = 3;

        sut.MasterPassword = "12345";
        sut.ConfirmPassword = "12345";
        sut.CanGoNext.Should().BeFalse();
        sut.ErrorMessage.Should().Contain("至少");

        sut.MasterPassword = "123456";
        sut.ConfirmPassword = "123456";
        sut.CanGoNext.Should().BeTrue();

        sut.MasterPassword = new string('x', 128);
        sut.ConfirmPassword = new string('x', 128);
        sut.CanGoNext.Should().BeTrue();

        sut.MasterPassword = new string('x', 129);
        sut.ConfirmPassword = new string('x', 129);
        sut.CanGoNext.Should().BeFalse();
        sut.ErrorMessage.Should().Contain("最多");
    }

    [Fact]
    public void RecoveryStep_EnablesFinishAfterRequestedWordMatches()
    {
        var sut = CreateViewModel();
        sut.CurrentStep = 3;
        sut.MasterPassword = "safe-123";
        sut.ConfirmPassword = "safe-123";

        sut.NextStepCommand.Execute(null);

        sut.CurrentStep.Should().Be(4);
        sut.CanGoNext.Should().BeFalse();
        sut.CanFinish.Should().BeFalse();
        sut.RecoveryKey.Should().NotBeNull();

        sut.VerificationWordInput = sut.RecoveryKey!.Words[sut.VerificationWordIndex];

        sut.CanFinish.Should().BeTrue();
        sut.CancelCommand.Execute(null);
    }

    private static SetupWizardViewModel CreateViewModel()
    {
        var crypto = new CryptoService();
        return new SetupWizardViewModel(
            new EncryptedVaultFileManager(crypto),
            new RecoveryKeyGenerator(),
            crypto);
    }
}
