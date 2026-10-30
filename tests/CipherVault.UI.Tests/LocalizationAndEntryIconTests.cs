namespace CipherVault.UI.Tests;

using CipherVault.Core.Models;
using CipherVault.UI.Converters;
using CipherVault.UI.Localization;
using CipherVault.UI.Views.Controls;
using FluentAssertions;
using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Xunit;

public sealed class LocalizationAndEntryIconTests
{
    [Theory]
    [InlineData("github", null, "G")]
    [InlineData("", "https://www.microsoft.com/account", "M")]
    [InlineData("Other", "https://example.com", "E")]
    [InlineData(null, null, "?")]
    [InlineData("密码", "https://example.com", "E")]
    public void EntryIcon_FallbackUsesWebsiteThenTitle(
        string? title,
        string? url,
        string expected)
    {
        EntryIcon.GetFallbackLetter(title, url).Should().Be(expected);
    }

    [Fact]
    public void LocalizationManager_NormalizesSupportedLanguages()
    {
        LocalizationManager.NormalizeLanguage("en-US").Should().Be("en-US");
        LocalizationManager.NormalizeLanguage("zh-CN").Should().Be("zh-CN");
        LocalizationManager.NormalizeLanguage("fr-FR").Should().Be("zh-CN");
        LocalizationManager.NormalizeLanguage(null).Should().Be("zh-CN");
    }

    [Fact]
    public void LocalizationManager_TranslatesEnglishAndChineseAtRuntime()
    {
        LocalizationManager manager = LocalizationManager.Instance;
        string original = manager.CurrentLanguage;

        try
        {
            manager.CurrentLanguage = LocalizationManager.English;
            manager.Translate("设置").Should().Be("Settings");
            manager.Format("{0} 个条目", 3).Should().Be("3 entries");

            manager.CurrentLanguage = LocalizationManager.SimplifiedChinese;
            manager.Translate("Settings").Should().Be("设置");
        }
        finally
        {
            manager.CurrentLanguage = original;
        }
    }

    [Fact]
    public void CredentialTypeDisplayConverter_LocalizesAllOptionsAndUsesOtherLabel()
    {
        LocalizationManager manager = LocalizationManager.Instance;
        string original = manager.CurrentLanguage;
        var converter = new CredentialTypeDisplayConverter();

        try
        {
            manager.CurrentLanguage = LocalizationManager.SimplifiedChinese;
            Enum.GetValues<CredentialType>()
                .Select(type => converter.Convert(
                    [type, manager.LanguageVersion],
                    typeof(string),
                    null!,
                    CultureInfo.InvariantCulture))
                .Should()
                .Equal("登录", "信用卡", "身份证", "安全笔记", "Wi-Fi", "服务器", "其它");

            manager.CurrentLanguage = LocalizationManager.English;
            Enum.GetValues<CredentialType>()
                .Select(type => converter.Convert(
                    [type, manager.LanguageVersion],
                    typeof(string),
                    null!,
                    CultureInfo.InvariantCulture))
                .Should()
                .Equal("Login", "Credit card", "ID card", "Secure note", "Wi-Fi", "Server", "Other");
        }
        finally
        {
            manager.CurrentLanguage = original;
        }
    }

    [Fact]
    public void LocalizationManager_TrackedWindowRefreshesAfterLiveLanguageChange()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            LocalizationManager manager = LocalizationManager.Instance;
            string original = manager.CurrentLanguage;
            Window? window = null;

            try
            {
                manager.CurrentLanguage = LocalizationManager.SimplifiedChinese;
                var title = new TextBlock { Text = "设置" };
                window = new Window
                {
                    Content = title,
                    Width = 200,
                    Height = 100,
                    Left = -10000,
                    Top = -10000,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                };
                manager.TrackWindow(window);
                window.Show();

                manager.CurrentLanguage = LocalizationManager.English;
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

                title.Text.Should().Be("Settings");
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                window?.Close();
                manager.CurrentLanguage = original;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        failure.Should().BeNull();
    }
}
