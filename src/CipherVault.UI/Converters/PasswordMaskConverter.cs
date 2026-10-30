namespace CipherVault.UI.Converters;

using System.Globalization;
using System.Windows.Data;

public class PasswordMaskConverter : IValueConverter
{
    private const string MaskedPassword = "\u2022\u2022\u2022\u2022\u2022\u2022\u2022\u2022"; // 8 bullet characters

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string str && !string.IsNullOrEmpty(str))
            return MaskedPassword;

        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
