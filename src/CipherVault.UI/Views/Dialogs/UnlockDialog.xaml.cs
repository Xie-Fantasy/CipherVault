namespace CipherVault.UI.Views.Dialogs;

using System.Security.Cryptography;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using CipherVault.UI.ViewModels;

public partial class UnlockDialog : Window
{
    private UnlockViewModel? _subscribedViewModel;
    public Action<Window>? ProtectSensitiveWindow { get; set; }

    public UnlockDialog()
    {
        InitializeComponent();
        CipherVault.UI.Localization.LocalizationManager.Instance.TrackWindow(this);
        Loaded += UnlockDialog_Loaded;
        DataContextChanged += UnlockDialog_DataContextChanged;
        Closed += UnlockDialog_Closed;
    }

    private void UnlockDialog_Loaded(object sender, RoutedEventArgs e)
    {
        MasterPasswordBox.Focus();
    }

    private void MasterPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is UnlockViewModel vm)
        {
            vm.MasterPassword = MasterPasswordBox.Password;
        }
    }

    private void MasterPasswordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is UnlockViewModel vm)
        {
            if (vm.UnlockCommand.CanExecute(null))
            {
                vm.UnlockCommand.Execute(null);
            }
        }
    }

    private void UnlockDialog_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_subscribedViewModel != null)
        {
            _subscribedViewModel.ForgotPasswordRequested -= OnForgotPasswordRequested;
            _subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _subscribedViewModel = e.NewValue as UnlockViewModel;
        if (_subscribedViewModel != null)
        {
            _subscribedViewModel.ForgotPasswordRequested += OnForgotPasswordRequested;
            _subscribedViewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void UnlockDialog_Closed(object? sender, EventArgs e)
    {
        if (_subscribedViewModel != null)
        {
            _subscribedViewModel.ForgotPasswordRequested -= OnForgotPasswordRequested;
            _subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }
        _subscribedViewModel = null;
        MasterPasswordBox.Clear();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UnlockViewModel.MasterPassword)
            && sender is UnlockViewModel { MasterPassword.Length: 0 }
            && MasterPasswordBox.Password.Length > 0)
        {
            MasterPasswordBox.Clear();
        }
    }

    private async void OnForgotPasswordRequested(object? sender, EventArgs e)
    {
        if (sender is not UnlockViewModel viewModel || viewModel.IsUnlocking)
            return;

        var recoveryDialog = new RecoveryKeyDialog
        {
            Owner = this,
        };
        recoveryDialog.SetupInputMode();
        ProtectSensitiveWindow?.Invoke(recoveryDialog);

        bool? accepted = recoveryDialog.ShowDialog();
        if (accepted != true || recoveryDialog.InputWords is not { Length: > 0 } words)
        {
            recoveryDialog.ClearSensitiveData();
            return;
        }

        byte[]? newMasterPassword = null;
        try
        {
            newMasterPassword = recoveryDialog.TakeNewMasterPasswordBytes();
            MasterPasswordBox.Clear();
            bool recovered = await viewModel.AttemptRecoveryUnlockAsync(words, newMasterPassword);
            if (!recovered && IsVisible)
                MasterPasswordBox.Focus();
        }
        finally
        {
            if (newMasterPassword is { Length: > 0 })
                CryptographicOperations.ZeroMemory(newMasterPassword);
            recoveryDialog.ClearSensitiveData();
        }
    }

    /// <summary>
    /// Clears the password box when the dialog is locked or error occurs.
    /// </summary>
    public void ClearPassword()
    {
        MasterPasswordBox.Clear();
        MasterPasswordBox.Focus();
    }
}
