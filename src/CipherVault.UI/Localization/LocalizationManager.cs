namespace CipherVault.UI.Localization;

using System.Globalization;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

/// <summary>
/// Applies a persisted UI language to existing and newly loaded WPF controls.
/// User-provided and data-bound content is never rewritten.
/// </summary>
public sealed class LocalizationManager : INotifyPropertyChanged
{
    public const string SimplifiedChinese = "zh-CN";
    public const string English = "en-US";

    private static readonly Lazy<LocalizationManager> LazyInstance = new(() => new());
    private string _currentLanguage = SimplifiedChinese;

    public static LocalizationManager Instance => LazyInstance.Value;

    public event EventHandler? LanguageChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    public int LanguageVersion { get; private set; }

    public string CurrentLanguage
    {
        get => _currentLanguage;
        set
        {
            string normalized = NormalizeLanguage(value);
            if (_currentLanguage.Equals(normalized, StringComparison.OrdinalIgnoreCase))
                return;

            _currentLanguage = normalized;
            LanguageVersion++;
            ApplyCulture();
            ApplyToOpenWindows();
            OnPropertyChanged(nameof(CurrentLanguage));
            OnPropertyChanged(nameof(IsEnglish));
            OnPropertyChanged(nameof(LanguageVersion));
            LanguageChanged?.Invoke(this, EventArgs.Empty);
            QueueDeferredRefresh();
        }
    }

    public bool IsEnglish => _currentLanguage.Equals(English, StringComparison.OrdinalIgnoreCase);

    private LocalizationManager()
    {
        EventManager.RegisterClassHandler(
            typeof(FrameworkElement),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnElementLoaded),
            handledEventsToo: true);
        ApplyCulture();
    }

    public static string NormalizeLanguage(string? language)
        => language?.Equals(English, StringComparison.OrdinalIgnoreCase) == true
            ? English
            : SimplifiedChinese;

    public string Translate(string source)
    {
        if (string.IsNullOrEmpty(source))
            return source;

        if (IsEnglish)
        {
            if (LocalizationTranslations.ChineseToEnglish.TryGetValue(source, out string? english))
                return english;
            return TranslatePatternToEnglish(source);
        }

        return LocalizationTranslations.EnglishToChinese.TryGetValue(source, out string? chinese)
            ? chinese
            : source;
    }

    public string Format(string chineseFormat, params object?[] args)
    {
        string format = Translate(chineseFormat);
        return string.Format(CultureInfo.CurrentCulture, format, args);
    }

    public void Refresh() => ApplyToOpenWindows();

    public void ApplyToElement(DependencyObject root)
    {
        ArgumentNullException.ThrowIfNull(root);
        ApplyTree(root, new HashSet<DependencyObject>());
    }

    public void TrackWindow(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        RoutedEventHandler? loaded = null;
        EventHandler? languageChanged = null;
        EventHandler? closed = null;

        void QueueWindowRefresh()
        {
            if (window.Dispatcher.HasShutdownStarted)
                return;

            window.Dispatcher.BeginInvoke(
                DispatcherPriority.DataBind,
                new Action(() => ApplyToElement(window)));
        }

        loaded = (_, _) => QueueWindowRefresh();
        languageChanged = (_, _) => QueueWindowRefresh();
        closed = (_, _) =>
        {
            window.Loaded -= loaded;
            window.Closed -= closed;
            LanguageChanged -= languageChanged;
        };

        window.Loaded += loaded;
        window.Closed += closed;
        LanguageChanged += languageChanged;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private void OnElementLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
            ApplyElement(element);
    }

    private void ApplyToOpenWindows()
    {
        if (Application.Current == null)
            return;

        void Apply()
        {
            foreach (Window window in Application.Current.Windows)
                ApplyTree(window, new HashSet<DependencyObject>());
        }

        if (Application.Current.Dispatcher.CheckAccess())
            Apply();
        else
            Application.Current.Dispatcher.Invoke(Apply);
    }

    private void QueueDeferredRefresh()
    {
        if (Application.Current == null)
            return;

        Application.Current.Dispatcher.BeginInvoke(
            DispatcherPriority.DataBind,
            new Action(ApplyToOpenWindows));
    }

    private void ApplyTree(DependencyObject root, HashSet<DependencyObject> visited)
    {
        if (!visited.Add(root))
            return;

        if (root is FrameworkElement element)
            ApplyElement(element);
        else if (root is Run run && !BindingOperations.IsDataBound(run, Run.TextProperty))
            run.SetCurrentValue(Run.TextProperty, Translate(run.Text));

        int visualChildCount = root is Visual or Visual3D
            ? VisualTreeHelper.GetChildrenCount(root)
            : 0;
        var visualChildren = new DependencyObject[visualChildCount];
        for (int index = 0; index < visualChildCount; index++)
            visualChildren[index] = VisualTreeHelper.GetChild(root, index);
        foreach (DependencyObject child in visualChildren)
            ApplyTree(child, visited);

        DependencyObject[] logicalChildren = LogicalTreeHelper.GetChildren(root)
            .OfType<DependencyObject>()
            .ToArray();
        foreach (DependencyObject child in logicalChildren)
            ApplyTree(child, visited);
    }

    private void ApplyElement(FrameworkElement element)
    {
        if (element is Window window && !BindingOperations.IsDataBound(window, Window.TitleProperty))
            window.SetCurrentValue(Window.TitleProperty, Translate(window.Title));

        if (element is TextBlock textBlock &&
            !BindingOperations.IsDataBound(textBlock, TextBlock.TextProperty) &&
            !HasStructuredInlines(textBlock))
        {
            textBlock.SetCurrentValue(TextBlock.TextProperty, Translate(textBlock.Text));
        }

        if (element is ContentControl contentControl &&
            contentControl.Content is string content &&
            !BindingOperations.IsDataBound(contentControl, ContentControl.ContentProperty))
        {
            contentControl.SetCurrentValue(ContentControl.ContentProperty, Translate(content));
        }

        if (element is HeaderedContentControl headeredControl &&
            headeredControl.Header is string header &&
            !BindingOperations.IsDataBound(headeredControl, HeaderedContentControl.HeaderProperty))
        {
            headeredControl.SetCurrentValue(HeaderedContentControl.HeaderProperty, Translate(header));
        }

        if (element.ToolTip is string toolTip &&
            !BindingOperations.IsDataBound(element, FrameworkElement.ToolTipProperty))
        {
            element.SetCurrentValue(FrameworkElement.ToolTipProperty, Translate(toolTip));
        }
    }

    private static bool HasStructuredInlines(TextBlock textBlock)
    {
        if (textBlock.Inlines.Count > 1)
            return true;

        return textBlock.Inlines.FirstInline is not Run run ||
               BindingOperations.IsDataBound(run, Run.TextProperty);
    }

    private void ApplyCulture()
    {
        CultureInfo culture = CultureInfo.GetCultureInfo(_currentLanguage);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }

    private static string TranslatePatternToEnglish(string source)
    {
        if (source.StartsWith("错误：", StringComparison.Ordinal))
            return "Error: " + source[3..];
        if (source.StartsWith("成功: ", StringComparison.Ordinal))
            return "Success: " + source[4..];
        if (source.StartsWith("失败: ", StringComparison.Ordinal))
            return "Failed: " + source[4..];
        if (source.EndsWith(" 项", StringComparison.Ordinal) &&
            int.TryParse(source[..^2], out int itemCount))
            return itemCount == 1 ? "1 item" : $"{itemCount} items";
        if (source.EndsWith(" 条记录", StringComparison.Ordinal) &&
            int.TryParse(source[..^4], out int recordCount))
            return recordCount == 1 ? "1 entry" : $"{recordCount} entries";
        if (source.EndsWith(" 分钟", StringComparison.Ordinal) &&
            int.TryParse(source[..^3], out int minutes))
            return minutes == 1 ? "1 minute" : $"{minutes} minutes";
        if (source.EndsWith(" 秒", StringComparison.Ordinal) &&
            int.TryParse(source[..^2], out int seconds))
            return seconds == 1 ? "1 second" : $"{seconds} seconds";
        return source;
    }
}
