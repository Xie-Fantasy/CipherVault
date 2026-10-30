namespace CipherVault.UI.Converters;

using CipherVault.Core.Models;
using CipherVault.UI.Helper;
using System.Globalization;
using System.Windows.Data;

public class CredentialTypeToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not CredentialType type)
            return Icon.FileText;

        return type switch
        {
            CredentialType.Login => Icon.User,
            CredentialType.CreditCard => Icon.Square,
            CredentialType.IdCard => Icon.FileText,
            CredentialType.SecureNote => Icon.Shield,
            CredentialType.WiFi => Icon.Square,
            CredentialType.Server => Icon.HardDrive,
            CredentialType.Custom => Icon.FileText,
            _ => Icon.FileText
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
