namespace CipherVault.UI.Converters;

using System.Globalization;
using System.Windows.Data;
using CipherVault.Core.Models;
using CipherVault.UI.Localization;

/// <summary>
/// Converts a persisted credential type into a localized, user-facing label.
/// The second multi-binding value exists to refresh open combo boxes when the
/// application language changes.
/// </summary>
public sealed class CredentialTypeDisplayConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length == 0 || values[0] is not CredentialType type)
            return string.Empty;

        string source = type switch
        {
            CredentialType.Login => "登录",
            CredentialType.CreditCard => "信用卡",
            CredentialType.IdCard => "身份证",
            CredentialType.SecureNote => "安全笔记",
            CredentialType.WiFi => "Wi-Fi",
            CredentialType.Server => "服务器",
            CredentialType.Custom => "其它",
            _ => "其它",
        };

        return LocalizationManager.Instance.Translate(source);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
