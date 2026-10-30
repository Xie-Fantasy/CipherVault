namespace CipherVault.UI.Views.Controls;

using System.Globalization;
using System.Windows;
using System.Windows.Controls;

/// <summary>
/// Displays the entry website/title initial.
/// </summary>
public partial class EntryIcon : UserControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title),
        typeof(string),
        typeof(EntryIcon),
        new PropertyMetadata(string.Empty, OnVisualSourceChanged));

    public static readonly DependencyProperty UrlProperty = DependencyProperty.Register(
        nameof(Url),
        typeof(string),
        typeof(EntryIcon),
        new PropertyMetadata(string.Empty, OnVisualSourceChanged));

    public string? Title
    {
        get => (string?)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Url
    {
        get => (string?)GetValue(UrlProperty);
        set => SetValue(UrlProperty, value);
    }

    public EntryIcon()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdateVisual();
        SizeChanged += (_, _) =>
        {
            FallbackLetter.FontSize = Math.Max(11, Math.Min(ActualWidth, ActualHeight) * 0.42);
            IconSurface.CornerRadius = new CornerRadius(Math.Max(4, Math.Min(ActualWidth, ActualHeight) * 0.2));
        };
    }

    private static void OnVisualSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((EntryIcon)d).UpdateVisual();

    private void UpdateVisual()
    {
        FallbackLetter.Text = GetFallbackLetter(Title, Url);
    }

    internal static string GetFallbackLetter(string? title, string? url)
    {
        string candidate = string.Empty;
        if (Uri.TryCreate(
                url?.StartsWith("http", StringComparison.OrdinalIgnoreCase) == true
                    ? url
                    : "https://" + url,
                UriKind.Absolute,
                out Uri? uri))
        {
            candidate = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
                ? uri.Host[4..]
                : uri.Host;
        }

        if (candidate.Length == 0)
            candidate = title?.Trim() ?? string.Empty;

        if (candidate.Length == 0)
            return "?";

        return StringInfo.GetNextTextElement(candidate).ToUpper(CultureInfo.CurrentUICulture);
    }
}
