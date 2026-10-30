namespace CipherVault.UI.Views;

using System.Windows;
using System.Windows.Input;
using CipherVault.UI.Helper;
using CipherVault.UI.ViewModels;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        CipherVault.UI.Localization.LocalizationManager.Instance.TrackWindow(this);
        Loaded += MainWindow_Loaded;
        StateChanged += MainWindow_StateChanged;
        PreviewKeyDown += MainWindow_PreviewKeyDown;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateMaximizeIcon();
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        UpdateMaximizeIcon();
    }

    private void UpdateMaximizeIcon()
    {
        if (MaximizeIconCtrl != null)
        {
            MaximizeIconCtrl.Icon = WindowState == WindowState.Maximized
                ? Helper.Icon.Copy     // Two overlapping rectangles indicate restore.
                : Helper.Icon.Square;  // Maximize icon
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control)
            return;

        if (e.Key == Key.F)
        {
            MainSearchBar.FocusSearch();
            e.Handled = true;
            return;
        }

        if (DataContext is not MainWindowViewModel viewModel)
            return;

        if (e.Key == Key.N && viewModel.CanAccessVault)
        {
            viewModel.EntryListVM.AddEntryCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.L && viewModel.CanAccessVault)
        {
            viewModel.LockVaultCommand.Execute(null);
            e.Handled = true;
        }
    }
}
