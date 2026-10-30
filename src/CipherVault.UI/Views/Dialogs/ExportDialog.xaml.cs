namespace CipherVault.UI.Views.Dialogs;

using System.Windows;
using CipherVault.Core.Interfaces;
using CipherVault.UI.ViewModels;
using Microsoft.Win32;
using CipherVault.UI.Localization;

/// <summary>
/// Code-behind for the export dialog window.
/// Handles file browser dialog requests and dialog lifecycle events.
/// </summary>
public partial class ExportDialog : Window
{
    public ExportDialog()
    {
        InitializeComponent();
        LocalizationManager.Instance.TrackWindow(this);
    }

    /// <summary>
    /// Initializes the dialog with the given ViewModel and wires up event handlers.
    /// </summary>
    /// <param name="viewModel">The export ViewModel to bind to.</param>
    public void Initialize(ExportViewModel viewModel)
    {
        DataContext = viewModel;

        viewModel.BrowseFileRequested += OnBrowseFileRequested;
        viewModel.ExportCompleted += OnExportCompleted;
        viewModel.Cancelled += OnCancelled;
    }

    private void OnBrowseFileRequested(object? sender, BrowseFileEventArgs e)
    {
        if (sender is not ExportViewModel vm)
            return;

        var filter = IExportService.GetFileFilter(e.Format);

        var dialog = new SaveFileDialog
        {
            Filter = filter + "|" + L.T("所有文件 (*.*)|*.*"),
            AddExtension = true,
            OverwritePrompt = true,
            Title = L.T("选择导出位置")
        };

        // Set default file name based on format
        dialog.FileName = e.Format switch
        {
            ExportFormat.EncryptedJson => "vault-export.cvault-export",
            ExportFormat.Csv => "vault-export.csv",
            ExportFormat.KeePassXml => "vault-export.xml",
            _ => "vault-export"
        };

        if (dialog.ShowDialog(this) == true)
        {
            vm.ExportFilePath = dialog.FileName;
        }
    }

    private void OnExportCompleted(object? sender, System.EventArgs e)
    {
        // Close after a brief delay so user can see the success message
        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = System.TimeSpan.FromSeconds(1.5)
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            DialogResult = true;
            Close();
        };
        timer.Start();
    }

    private void OnCancelled(object? sender, System.EventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
