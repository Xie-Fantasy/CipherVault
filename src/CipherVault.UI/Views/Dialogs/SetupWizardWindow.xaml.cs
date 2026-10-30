namespace CipherVault.UI.Views.Dialogs;

using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using CipherVault.UI.ViewModels;
using Microsoft.Win32;
using CipherVault.UI.Localization;

public partial class SetupWizardWindow : Window
{
    private SetupWizardViewModel? _viewModel;

    public SetupWizardWindow()
    {
        InitializeComponent();
        LocalizationManager.Instance.TrackWindow(this);
        DataContextChanged += OnDataContextChanged;
        Closed += OnClosed;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        DetachViewModel();
        _viewModel = e.NewValue as SetupWizardViewModel;
        if (_viewModel == null)
            return;

        _viewModel.BrowseLocationRequested += OnBrowseLocationRequested;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        DetachViewModel();
        DataContextChanged -= OnDataContextChanged;
        Closed -= OnClosed;
    }

    private void DetachViewModel()
    {
        if (_viewModel == null)
            return;

        _viewModel.BrowseLocationRequested -= OnBrowseLocationRequested;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel = null;
    }

    private void OnBrowseLocationRequested(object? sender, EventArgs e)
    {
        if (_viewModel == null)
            return;

        string fileName = BuildSafeVaultFileName(_viewModel.VaultName);
        string initialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (!string.IsNullOrWhiteSpace(_viewModel.VaultFilePath))
        {
            string? currentDirectory = Path.GetDirectoryName(_viewModel.VaultFilePath);
            if (!string.IsNullOrWhiteSpace(currentDirectory) && Directory.Exists(currentDirectory))
                initialDirectory = currentDirectory;

            string currentFileName = Path.GetFileName(_viewModel.VaultFilePath);
            if (!string.IsNullOrWhiteSpace(currentFileName))
                fileName = currentFileName;
        }

        var dialog = new SaveFileDialog
        {
            Title = L.T("选择保险库保存位置"),
            Filter = L.T("CipherVault 保险库 (*.cvault)|*.cvault"),
            DefaultExt = ".cvault",
            AddExtension = true,
            CheckPathExists = true,
            OverwritePrompt = false,
            ValidateNames = true,
            InitialDirectory = initialDirectory,
            FileName = fileName,
        };

        if (dialog.ShowDialog(this) != true)
            return;

        if (File.Exists(dialog.FileName))
        {
            MessageBox.Show(
                this,
                L.T("所选位置已经存在同名保险库。为避免覆盖数据，请选择其他名称或位置。"),
                L.T("无法覆盖现有保险库"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        _viewModel.UseSelectedVaultFilePath(dialog.FileName);
    }

    private static string BuildSafeVaultFileName(string vaultName)
    {
        string name = string.IsNullOrWhiteSpace(vaultName) ? L.T("我的保险库") : vaultName.Trim();
        foreach (char invalidCharacter in Path.GetInvalidFileNameChars())
            name = name.Replace(invalidCharacter, '_');

        return $"{name}.cvault";
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SetupWizardViewModel.CurrentStep)
            || _viewModel?.CurrentStep != 4)
        {
            return;
        }

        Dispatcher.BeginInvoke(
            () =>
            {
                VerificationWordTextBox.Focus();
                Keyboard.Focus(VerificationWordTextBox);
            },
            DispatcherPriority.Input);
    }

    private void SetupPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is SetupWizardViewModel vm)
        {
            vm.MasterPassword = SetupPasswordBox.Password;
        }
    }

    private void ConfirmPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is SetupWizardViewModel vm)
        {
            vm.ConfirmPassword = ConfirmPasswordBox.Password;
        }
    }

    /// <summary>
    /// Clears the password boxes when navigating back.
    /// </summary>
    public void ClearPasswordBoxes()
    {
        SetupPasswordBox.Clear();
        ConfirmPasswordBox.Clear();
    }
}
