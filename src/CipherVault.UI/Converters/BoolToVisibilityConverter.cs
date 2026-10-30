namespace CipherVault.UI.Converters;

using System.Globalization;
using System.Windows;
using System.Windows.Data;

public class BoolToVisibilityConverter : IValueConverter
{
    public bool IsInverted { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var boolValue = value is bool b && b;
        if (IsInverted) boolValue = !boolValue;
        return boolValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var visibility = value is Visibility v ? v : Visibility.Collapsed;
        var result = visibility == Visibility.Visible;
        return IsInverted ? !result : result;
    }
}
