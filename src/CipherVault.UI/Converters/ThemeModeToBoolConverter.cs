namespace CipherVault.UI.Converters;

using System.Globalization;
using System.Windows.Data;
using CipherVault.Core.Enums;

/// <summary>
/// Converts a ThemeMode enum value to a boolean for RadioButton binding.
/// The ConverterParameter specifies which ThemeMode value should return true.
/// </summary>
public class ThemeModeToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ThemeMode themeMode && parameter is string paramStr)
        {
            if (Enum.TryParse<ThemeMode>(paramStr, true, out var targetMode))
            {
                return themeMode == targetMode;
            }
        }
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue && boolValue && parameter is string paramStr)
        {
            if (Enum.TryParse<ThemeMode>(paramStr, true, out var targetMode))
            {
                return targetMode;
            }
        }
        return ThemeMode.System;
    }
}
