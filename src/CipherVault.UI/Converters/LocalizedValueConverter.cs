namespace CipherVault.UI.Converters;

using System.Globalization;
using System.Windows.Data;
using CipherVault.UI.Localization;

public sealed class LocalizedValueConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        string value = values.Length > 0 ? values[0]?.ToString() ?? string.Empty : string.Empty;
        return LocalizationManager.Instance.Translate(value);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
