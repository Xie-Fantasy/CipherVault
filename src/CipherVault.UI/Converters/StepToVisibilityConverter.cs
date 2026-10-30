namespace CipherVault.UI.Converters;

using System.Globalization;
using System.Windows;
using System.Windows.Data;

/// <summary>
/// Converts a CurrentStep integer to Visibility based on a matching step number.
/// The ConverterParameter specifies which step number should be visible.
/// </summary>
public class StepToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int currentStep && parameter is string paramStr)
        {
            if (int.TryParse(paramStr, out var targetStep))
            {
                return currentStep == targetStep ? Visibility.Visible : Visibility.Collapsed;
            }
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}

/// <summary>
/// Converts IsImportFinished boolean to Visibility for the result panel.
/// </summary>
public class ImportFinishedToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool finished && finished)
            return Visibility.Visible;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}

/// <summary>
/// Converts IsImportFinished boolean to inverse Visibility (hides content when import is finished).
/// </summary>
public class ImportFinishedToInverseVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool finished && finished)
            return Visibility.Collapsed;
        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}
