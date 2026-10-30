namespace CipherVault.UI.Themes;

using CipherVault.Core.Enums;
using System.Windows;

public sealed class ThemeManager
{
    private static ThemeManager? _instance;
    public static ThemeManager Instance => _instance ??= new ThemeManager();

    private ThemeMode _currentMode = ThemeMode.System;
    private ResourceDictionary? _currentThemeDict;

    private ThemeManager()
    {
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public ThemeMode CurrentMode
    {
        get => _currentMode;
        set
        {
            _currentMode = value;
            ApplyTheme();
        }
    }

    public bool IsDarkMode { get; private set; }

    public event Action? ThemeChanged;

    public void ApplyFontSize(string? size)
    {
        double scale = size?.ToLowerInvariant() switch
        {
            "small" => 0.9,
            "large" => 1.15,
            _ => 1.0,
        };

        if (Application.Current == null)
            return;

        var resources = Application.Current.Resources;
        resources["FontSizeDisplay"] = 28d * scale;
        resources["FontSizeTitle"] = 20d * scale;
        resources["FontSizeSubtitle"] = 16d * scale;
        resources["FontSizeBody"] = 14d * scale;
        resources["FontSizeCaption"] = 12d * scale;
        resources["FontSizeSmall"] = 11d * scale;
        resources["FontSizePassword"] = 15d * scale;
    }

    public void ApplyTheme()
    {
        var isDark = _currentMode switch
        {
            ThemeMode.Dark => true,
            ThemeMode.Light => false,
            _ => IsSystemDarkMode()
        };

        IsDarkMode = isDark;
        if (Application.Current == null)
        {
            ThemeChanged?.Invoke();
            return;
        }

        Uri themeUri = GetThemeResourceUri(isDark);

        var newDict = new ResourceDictionary { Source = themeUri };

        var appDicts = Application.Current.Resources.MergedDictionaries;
        _currentThemeDict ??= appDicts.FirstOrDefault(dictionary =>
            dictionary.Source?.OriginalString.EndsWith("/Themes/Colors.xaml", StringComparison.OrdinalIgnoreCase) == true ||
            dictionary.Source?.OriginalString.EndsWith("/Themes/DarkColors.xaml", StringComparison.OrdinalIgnoreCase) == true ||
            dictionary.Source?.OriginalString.EndsWith("Themes/Colors.xaml", StringComparison.OrdinalIgnoreCase) == true ||
            dictionary.Source?.OriginalString.EndsWith("Themes/DarkColors.xaml", StringComparison.OrdinalIgnoreCase) == true);
        if (_currentThemeDict != null)
        {
            var index = appDicts.IndexOf(_currentThemeDict);
            if (index >= 0)
            {
                appDicts.Insert(index, newDict);
                appDicts.Remove(_currentThemeDict);
            }
            else
            {
                appDicts.Add(newDict);
            }
        }
        else
        {
            appDicts.Add(newDict);
        }

        _currentThemeDict = newDict;
        ThemeChanged?.Invoke();
    }

    internal static Uri GetThemeResourceUri(bool isDark)
    {
        string resourceName = isDark ? "DarkColors.xaml" : "Colors.xaml";
        return new Uri(
            $"/CipherVault.UI;component/Themes/{resourceName}",
            UriKind.Relative);
    }

    private void OnUserPreferenceChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        if (_currentMode != ThemeMode.System || Application.Current == null)
            return;

        Application.Current.Dispatcher.BeginInvoke(ApplyTheme);
    }

    private static bool IsSystemDarkMode()
    {
        try
        {
            var key = Microsoft.Win32.Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme", 1);
            return key is int val && val == 0;
        }
        catch
        {
            return false;
        }
    }
}
