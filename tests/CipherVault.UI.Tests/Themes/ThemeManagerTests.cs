namespace CipherVault.UI.Tests.Themes;

using System.Windows;
using CipherVault.UI.Themes;
using FluentAssertions;
using Xunit;

public sealed class ThemeManagerTests
{
    [Theory]
    [InlineData(false, "Colors.xaml")]
    [InlineData(true, "DarkColors.xaml")]
    public void ThemeResourceUriTargetsUiAssemblyAndExistingResource(bool isDark, string fileName)
    {
        Uri uri = ThemeManager.GetThemeResourceUri(isDark);

        uri.IsAbsoluteUri.Should().BeFalse();
        uri.OriginalString.Should().Be(
            $"/CipherVault.UI;component/Themes/{fileName}");
        Application.GetResourceStream(uri).Should().NotBeNull();
    }
}
