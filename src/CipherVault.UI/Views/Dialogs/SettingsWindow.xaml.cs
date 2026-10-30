namespace CipherVault.UI.Views.Dialogs;

using Microsoft.Win32;
using CipherVault.UI.Localization;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CipherVault.UI.ViewModels;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;
    private bool _allowClose;
    private bool _closeRequestQueued;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        LocalizationManager.Instance.TrackWindow(this);
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = _viewModel;

        _viewModel.CloseRequested += OnCloseRequested;
        _viewModel.BrowseBackupDirectoryRequested += OnBrowseBackupDirectoryRequested;
        Closed += OnClosed;
    }

    private void OnBrowseBackupDirectoryRequested(object? sender, EventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = L.T("选择备份目录"),
            InitialDirectory = _viewModel.BackupDirectory,
            Multiselect = false,
        };
        if (dialog.ShowDialog(this) == true)
            _viewModel.BackupDirectory = dialog.FolderName;
    }

    private void OnCloseRequested(object? sender, EventArgs e)
    {
        _allowClose = true;
        Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
        => _viewModel.CloseCommand.Execute(null);

    private void CancelButton_Click(object sender, RoutedEventArgs e)
        => _viewModel.CloseCommand.Execute(null);

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
        => await _viewModel.SaveCommand.ExecuteAsync(null);

    private void SettingsWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
            return;

        e.Cancel = true;
        if (_closeRequestQueued)
            return;

        _closeRequestQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
        {
            _closeRequestQueued = false;
            _viewModel.CloseCommand.Execute(null);
        }));
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.CloseRequested -= OnCloseRequested;
        _viewModel.BrowseBackupDirectoryRequested -= OnBrowseBackupDirectoryRequested;
        Closed -= OnClosed;
    }

    private void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb || rb.Tag is not string tag)
            return;

        // Hide all panels
        SecurityPanel.Visibility = Visibility.Collapsed;
        AppearancePanel.Visibility = Visibility.Collapsed;
        BackupPanel.Visibility = Visibility.Collapsed;
        HotkeyPanel.Visibility = Visibility.Collapsed;
        TrayPanel.Visibility = Visibility.Collapsed;

        // Show the selected panel
        switch (tag)
        {
            case "Security":
                SecurityPanel.Visibility = Visibility.Visible;
                break;
            case "Appearance":
                AppearancePanel.Visibility = Visibility.Visible;
                break;
            case "Backup":
                BackupPanel.Visibility = Visibility.Visible;
                break;
            case "Hotkey":
                HotkeyPanel.Visibility = Visibility.Visible;
                break;
            case "Tray":
                TrayPanel.Visibility = Visibility.Visible;
                break;
        }
    }
}
