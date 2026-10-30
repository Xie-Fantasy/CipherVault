namespace CipherVault.UI.Converters;

using System.Globalization;
using System.Windows.Data;
using CipherVault.UI.Localization;

public sealed class LocalizedFormatConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        string format = parameter as string ?? "{0}";
        object? value = values.Length > 0 ? values[0] : null;
        return L.F(format, value);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
