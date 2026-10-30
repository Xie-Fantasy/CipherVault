namespace CipherVault.UI.Views.Controls;

using System.Collections;
using System.Windows;
using System.Windows.Controls;

public partial class CustomFieldsEditor : UserControl
{
    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(
            nameof(ItemsSource),
            typeof(IEnumerable),
            typeof(CustomFieldsEditor),
            new PropertyMetadata(null));

    public static readonly DependencyProperty IsEditingProperty =
        DependencyProperty.Register(
            nameof(IsEditing),
            typeof(bool),
            typeof(CustomFieldsEditor),
            new PropertyMetadata(false));

    public IEnumerable ItemsSource
    {
        get => (IEnumerable)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public bool IsEditing
    {
        get => (bool)GetValue(IsEditingProperty);
        set => SetValue(IsEditingProperty, value);
    }

    public CustomFieldsEditor()
    {
        InitializeComponent();
    }

}
