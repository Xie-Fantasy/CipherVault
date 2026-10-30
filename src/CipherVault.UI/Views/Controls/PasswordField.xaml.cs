namespace CipherVault.UI.Views.Controls;

using System.Windows;
using System.Windows.Controls;

public partial class PasswordField : UserControl
{
    public static readonly DependencyProperty PasswordTextProperty =
        DependencyProperty.Register(
            nameof(PasswordText),
            typeof(string),
            typeof(PasswordField),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty IsPasswordVisibleProperty =
        DependencyProperty.Register(
            nameof(IsPasswordVisible),
            typeof(bool),
            typeof(PasswordField),
            new PropertyMetadata(false));

    public string PasswordText
    {
        get => (string)GetValue(PasswordTextProperty);
        set => SetValue(PasswordTextProperty, value);
    }

    public bool IsPasswordVisible
    {
        get => (bool)GetValue(IsPasswordVisibleProperty);
        set => SetValue(IsPasswordVisibleProperty, value);
    }

    public PasswordField()
    {
        InitializeComponent();
    }
}
