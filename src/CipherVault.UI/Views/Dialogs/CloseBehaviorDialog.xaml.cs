namespace CipherVault.UI.Views.Dialogs;

using System.Windows;
using CipherVault.UI.Localization;

public enum MainWindowCloseChoice
{
    MinimizeToTray,
    ExitApplication,
}

public partial class CloseBehaviorDialog : Window
{
    public MainWindowCloseChoice SelectedChoice { get; private set; } =
        MainWindowCloseChoice.MinimizeToTray;

    public bool RememberSelection => RememberChoice.IsChecked == true;

    public CloseBehaviorDialog()
    {
        InitializeComponent();
        LocalizationManager.Instance.TrackWindow(this);
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        SelectedChoice = ExitChoice.IsChecked == true
            ? MainWindowCloseChoice.ExitApplication
            : MainWindowCloseChoice.MinimizeToTray;
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
