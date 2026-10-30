namespace CipherVault.UI.Views.Controls;

using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CipherVault.Core.Models;
using CipherVault.UI.ViewModels;

public partial class TagEditor : UserControl
{
    public static readonly DependencyProperty TagsProperty =
        DependencyProperty.Register(
            nameof(Tags),
            typeof(IEnumerable),
            typeof(TagEditor),
            new PropertyMetadata(null));

    public static readonly DependencyProperty IsEditingProperty =
        DependencyProperty.Register(
            nameof(IsEditing),
            typeof(bool),
            typeof(TagEditor),
            new PropertyMetadata(false));

    public IEnumerable Tags
    {
        get => (IEnumerable)GetValue(TagsProperty);
        set => SetValue(TagsProperty, value);
    }

    public bool IsEditing
    {
        get => (bool)GetValue(IsEditingProperty);
        set => SetValue(IsEditingProperty, value);
    }

    public TagEditor()
    {
        InitializeComponent();
    }

    private void AddTagButton_Click(object sender, RoutedEventArgs e)
    {
        AddTagButton.Visibility = Visibility.Collapsed;
        TagInputPanel.Visibility = Visibility.Visible;
        NewTagInput.Text = string.Empty;
        NewTagInput.Focus();
    }

    private void ConfirmTag_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is EntryDetailViewModel vm && !string.IsNullOrWhiteSpace(NewTagInput.Text) &&
            vm.AddTagCommand.CanExecute(NewTagInput.Text.Trim()))
            vm.AddTagCommand.Execute(NewTagInput.Text.Trim());
        HideInput();
    }

    private void CancelTag_Click(object sender, RoutedEventArgs e)
    {
        HideInput();
    }

    private void NewTagInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ConfirmTag_Click(sender, e);
        }
        else if (e.Key == Key.Escape)
        {
            HideInput();
        }
    }

    private void RemoveTag_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is EntryDetailViewModel vm && sender is Button btn && btn.Tag is Tag tag &&
            vm.RemoveTagCommand.CanExecute(tag))
            vm.RemoveTagCommand.Execute(tag);
    }

    private void HideInput()
    {
        TagInputPanel.Visibility = Visibility.Collapsed;
        AddTagButton.Visibility = Visibility.Visible;
    }
}
