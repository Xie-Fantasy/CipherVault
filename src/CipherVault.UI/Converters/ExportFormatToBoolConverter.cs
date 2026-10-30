namespace CipherVault.UI.Converters;

using System.Globalization;
using System.Windows.Data;
using CipherVault.Core.Interfaces;

/// <summary>
/// Converts an ExportFormat enum value to a boolean for RadioButton binding.
/// The ConverterParameter specifies which ExportFormat value should return true.
/// </summary>
public class ExportFormatToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ExportFormat format && parameter is string paramStr)
        {
            if (Enum.TryParse<ExportFormat>(paramStr, true, out var targetFormat))
            {
                return format == targetFormat;
            }
        }
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue && boolValue && parameter is string paramStr)
        {
            if (Enum.TryParse<ExportFormat>(paramStr, true, out var targetFormat))
            {
                return targetFormat;
            }
        }
        return ExportFormat.EncryptedJson;
    }
}
