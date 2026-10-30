namespace CipherVault.UI.Views.Controls;

using System.Windows;
using System.Windows.Controls;

public partial class SearchBarControl : UserControl
{
    public static readonly DependencyProperty SearchQueryProperty =
        DependencyProperty.Register(
            nameof(SearchQuery),
            typeof(string),
            typeof(SearchBarControl),
            new FrameworkPropertyMetadata(
                string.Empty,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public string SearchQuery
    {
        get => (string)GetValue(SearchQueryProperty);
        set => SetValue(SearchQueryProperty, value);
    }

    public SearchBarControl()
    {
        InitializeComponent();
    }

    public void FocusSearch()
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void SearchBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb && tb.Text == string.Empty)
        {
            // Placeholder is handled by XAML triggers
        }
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = string.Empty;
        SearchBox.Focus();
    }
}
