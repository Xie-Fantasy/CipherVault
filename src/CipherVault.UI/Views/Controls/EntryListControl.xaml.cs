namespace CipherVault.UI.Views.Controls;

using CipherVault.Core.Models;
using CipherVault.UI.Localization;
using CipherVault.UI.ViewModels;
using CipherVault.UI.Views.Dialogs;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

public partial class EntryListControl : UserControl
{
    public EntryListControl()
    {
        InitializeComponent();
    }

    private void EntriesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is EntryListViewModel viewModel)
            viewModel.UpdateSelection(EntriesList.SelectedItems.OfType<VaultEntry>());
    }

    private void SelectAllButton_Click(object sender, RoutedEventArgs e)
        => EntriesList.SelectAll();

    private void ClearSelectionButton_Click(object sender, RoutedEventArgs e)
        => EntriesList.UnselectAll();

    private void SortButton_Click(object sender, RoutedEventArgs e)
        => SortPopup.IsOpen = !SortPopup.IsOpen;

    private void SortOptionButton_Click(object sender, RoutedEventArgs e)
        => SortPopup.IsOpen = false;

    private void BulkModeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { IsChecked: false })
            ExitBulkMode();
    }

    private void ExitBulkModeButton_Click(object sender, RoutedEventArgs e)
        => ExitBulkMode();

    private void ExitBulkMode()
    {
        if (DataContext is not EntryListViewModel viewModel)
            return;

        VaultEntry? current = viewModel.SelectedEntry;
        viewModel.IsBulkMode = false;
        EntriesList.UnselectAll();
        if (current != null && viewModel.Entries.Any(entry => entry.Id == current.Id))
            EntriesList.SelectedItem = current;
    }

    private void DeleteSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not EntryListViewModel viewModel || viewModel.SelectedCount == 0)
            return;

        Window? owner = Window.GetWindow(this);
        if (owner == null || !ConfirmDialog.Show(
                owner,
                L.T("批量删除条目"),
                L.F("确定要将所选 {0} 个条目移到回收站吗？", viewModel.SelectedCount),
                ConfirmDialog.DialogIcon.Danger,
                ConfirmDialog.ButtonStyle.YesNo))
        {
            return;
        }

        if (viewModel.DeleteSelectedCommand.CanExecute(null))
            viewModel.DeleteSelectedCommand.Execute(null);
    }

    private void PermanentlyDeleteSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not EntryListViewModel viewModel || viewModel.SelectedCount == 0)
            return;

        Window? owner = Window.GetWindow(this);
        if (owner == null || !ConfirmDialog.Show(
                owner,
                L.T("永久删除所选条目"),
                L.F("永久删除所选 {0} 个条目？此操作无法撤销。", viewModel.SelectedCount),
                ConfirmDialog.DialogIcon.Danger,
                ConfirmDialog.ButtonStyle.YesNo))
        {
            return;
        }

        if (viewModel.PermanentlyDeleteSelectedCommand.CanExecute(null))
            viewModel.PermanentlyDeleteSelectedCommand.Execute(null);
    }
}
