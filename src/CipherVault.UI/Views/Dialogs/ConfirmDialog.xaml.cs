namespace CipherVault.UI.Views.Dialogs;

using System.Windows;
using CipherVault.UI.Localization;

public partial class ConfirmDialog : Window
{
    public enum DialogIcon { Warning, Question, Info, Danger }
    public enum ButtonStyle { OkCancel, YesNo }

    public bool IsConfirmed { get; private set; }

    public ConfirmDialog()
    {
        InitializeComponent();
        LocalizationManager.Instance.TrackWindow(this);
    }

    /// <summary>
    /// Configures the dialog with the given parameters.
    /// </summary>
    /// <param name="title">Dialog title</param>
    /// <param name="message">Dialog message body</param>
    /// <param name="icon">Icon type to display</param>
    /// <param name="buttonStyle">Button label style</param>
    public void Setup(string title, string message, DialogIcon icon = DialogIcon.Warning,
                      ButtonStyle buttonStyle = ButtonStyle.OkCancel)
    {
        TitleText.Text = title;
        MessageText.Text = message;

        // Set icon
        IconText.Icon = icon switch
        {
            DialogIcon.Warning => Helper.Icon.AlertTriangle,
            DialogIcon.Question => Helper.Icon.Info,
            DialogIcon.Info => Helper.Icon.Info,
            DialogIcon.Danger => Helper.Icon.AlertTriangle,
            _ => Helper.Icon.AlertTriangle
        };

        IconText.Foreground = icon switch
        {
            DialogIcon.Warning => (System.Windows.Media.Brush)FindResource("BrushWarning"),
            DialogIcon.Question => (System.Windows.Media.Brush)FindResource("BrushPrimary"),
            DialogIcon.Info => (System.Windows.Media.Brush)FindResource("BrushPrimary"),
            DialogIcon.Danger => (System.Windows.Media.Brush)FindResource("BrushDanger"),
            _ => (System.Windows.Media.Brush)FindResource("BrushWarning")
        };

        // Set button labels
        if (buttonStyle == ButtonStyle.YesNo)
        {
            CancelButton.Content = L.T("否");
            ConfirmButton.Content = L.T("是");
        }

        // Style confirm button red for danger
        if (icon == DialogIcon.Danger)
        {
            ConfirmButton.Background = (System.Windows.Media.Brush)FindResource("BrushDanger");
        }
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        IsConfirmed = true;
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        IsConfirmed = false;
        DialogResult = false;
        Close();
    }

    /// <summary>
    /// Shows a confirmation dialog and returns whether the user confirmed.
    /// </summary>
    public static bool Show(Window owner, string title, string message,
                            DialogIcon icon = DialogIcon.Warning,
                            ButtonStyle buttonStyle = ButtonStyle.OkCancel)
    {
        var dialog = new ConfirmDialog
        {
            Owner = owner
        };
        dialog.Setup(title, message, icon, buttonStyle);
        return dialog.ShowDialog() == true;
    }
}
