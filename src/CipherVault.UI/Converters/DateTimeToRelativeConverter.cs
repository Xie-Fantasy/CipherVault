namespace CipherVault.UI.Converters;

using System.Globalization;
using System.Windows.Data;
using CipherVault.UI.Localization;

public class DateTimeToRelativeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not DateTime dateTime || dateTime == default)
            return string.Empty;

        var span = DateTime.Now - dateTime;

        if (span.TotalSeconds < 60)
            return L.T("刚刚");

        if (span.TotalMinutes < 60)
            return L.F("{0} 分钟前", (int)span.TotalMinutes);

        if (span.TotalHours < 24)
            return L.F("{0} 小时前", (int)span.TotalHours);

        if (span.TotalDays < 2)
            return L.T("昨天");

        if (span.TotalDays < 30)
            return L.F("{0} 天前", (int)span.TotalDays);

        return dateTime.ToString("yyyy-MM-dd");
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
