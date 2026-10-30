namespace CipherVault.UI.Views.Controls;

using CipherVault.UI.ViewModels;
using CipherVault.UI.Views.Dialogs;
using System.Windows;
using System.Windows.Controls;
using CipherVault.UI.Localization;

public partial class EntryDetailPanel : UserControl
{
    public EntryDetailPanel()
    {
        InitializeComponent();
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not EntryDetailViewModel viewModel || viewModel.CurrentEntry is null)
        {
            return;
        }

        var owner = Window.GetWindow(this);
        if (owner is null || !ConfirmDialog.Show(
                owner,
                L.T("删除条目"),
                L.F("确定要将“{0}”移到回收站吗？", viewModel.CurrentEntry.Title),
                ConfirmDialog.DialogIcon.Danger,
                ConfirmDialog.ButtonStyle.YesNo))
        {
            return;
        }

        if (viewModel.DeleteCommand.CanExecute(null))
        {
            viewModel.DeleteCommand.Execute(null);
        }
    }

    private void PermanentDeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not EntryDetailViewModel viewModel || viewModel.CurrentEntry is null)
            return;

        var owner = Window.GetWindow(this);
        if (owner is null || !ConfirmDialog.Show(
                owner,
                L.T("永久删除条目"),
                L.F("永久删除“{0}”？此操作无法撤销。", viewModel.CurrentEntry.Title),
                ConfirmDialog.DialogIcon.Danger,
                ConfirmDialog.ButtonStyle.YesNo))
            return;

        if (viewModel.PermanentlyDeleteCommand.CanExecute(null))
            viewModel.PermanentlyDeleteCommand.Execute(null);
    }
}
