namespace CipherVault.UI.Views.Dialogs;

using System.Collections.Generic;
using System.Text;
using System.Windows;
using CipherVault.UI.Localization;
using CipherVault.Core.Security;
using CipherVault.Infrastructure.Crypto;

public partial class RecoveryKeyDialog : Window
{
    public enum DialogMode { Display, Input }

    private DialogMode _mode = DialogMode.Display;
    public string[]? InputWords { get; private set; }
    public bool IsConfirmed { get; private set; }

    public RecoveryKeyDialog()
    {
        InitializeComponent();
        LocalizationManager.Instance.TrackWindow(this);
    }

    /// <summary>
    /// Configures the dialog for display mode with the given recovery words.
    /// </summary>
    public void SetupDisplayMode(IEnumerable<object> words)
    {
        _mode = DialogMode.Display;
        TitleText.Text = L.T("恢复密钥");
        DisplayPanel.Visibility = Visibility.Visible;
        InputPanel.Visibility = Visibility.Collapsed;
        ConfirmButton.Content = L.T("我已抄写");
        ConfirmButton.Visibility = Visibility.Visible;
        WordGrid.ItemsSource = words;
    }

    /// <summary>
    /// Configures the dialog for input mode (user enters recovery words).
    /// </summary>
    public void SetupInputMode()
    {
        _mode = DialogMode.Input;
        TitleText.Text = L.T("输入恢复密钥");
        DisplayPanel.Visibility = Visibility.Collapsed;
        InputPanel.Visibility = Visibility.Visible;
        ConfirmButton.Content = L.T("确认恢复");
        ConfirmButton.Visibility = Visibility.Visible;
        RecoveryInput.Focus();
    }

    /// <summary>
    /// Shows an error message in input mode.
    /// </summary>
    public void ShowInputError(string message)
    {
        InputError.Text = message;
        InputError.Visibility = Visibility.Visible;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        IsConfirmed = false;
        ClearSensitiveData();
        DialogResult = false;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        IsConfirmed = false;
        ClearSensitiveData();
        DialogResult = false;
        Close();
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        if (_mode == DialogMode.Input)
        {
            var text = RecoveryInput.Text.Trim();
            if (string.IsNullOrEmpty(text))
            {
                ShowInputError(L.T("请输入恢复密钥词。"));
                return;
            }

            InputWords = text.Split(new[] { ' ', '\t', '\n', '\r' },
                StringSplitOptions.RemoveEmptyEntries);

            RecoveryInput.Clear();

            if (InputWords.Length != RecoveryKeyGenerator.MnemonicWordCount)
            {
                ShowInputError(L.F(
                    "请输入 {0} 个词（当前输入了 {1} 个）。",
                    RecoveryKeyGenerator.MnemonicWordCount,
                    InputWords.Length));
                Array.Clear(InputWords);
                InputWords = null;
                return;
            }

            if (!MasterPasswordPolicy.IsValid(NewPasswordInput.Password))
            {
                ShowInputError(L.F(
                    "新的主密码需要 {0}–{1} 个字符。",
                    MasterPasswordPolicy.MinimumLength,
                    MasterPasswordPolicy.MaximumLength));
                Array.Clear(InputWords);
                InputWords = null;
                return;
            }

            if (!NewPasswordInput.Password.Equals(ConfirmPasswordInput.Password, StringComparison.Ordinal))
            {
                ShowInputError(L.T("两次输入的主密码不一致。"));
                Array.Clear(InputWords);
                InputWords = null;
                return;
            }

            IsConfirmed = true;
            DialogResult = true;
            Close();
        }
        else
        {
            IsConfirmed = true;
            DialogResult = true;
            Close();
        }
    }

    /// <summary>
    /// Clears recovery input retained by the dialog after the caller has finished
    /// attempting recovery.
    /// </summary>
    public void ClearSensitiveData()
    {
        RecoveryInput.Clear();
        NewPasswordInput.Clear();
        ConfirmPasswordInput.Clear();
        if (InputWords != null)
        {
            Array.Clear(InputWords);
            InputWords = null;
        }

        WordGrid.ItemsSource = null;
    }

    public byte[] TakeNewMasterPasswordBytes()
    {
        byte[] password = Encoding.UTF8.GetBytes(NewPasswordInput.Password);
        NewPasswordInput.Clear();
        ConfirmPasswordInput.Clear();
        return password;
    }
}
