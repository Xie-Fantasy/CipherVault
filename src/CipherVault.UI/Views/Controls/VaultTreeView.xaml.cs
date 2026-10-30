namespace CipherVault.UI.Views.Controls;

using System.Windows;
using CipherVault.UI.Localization;
using System.Windows.Controls;
using System.Windows.Input;
using CipherVault.Core.Models;
using CipherVault.UI.ViewModels;

public partial class VaultTreeView : UserControl
{
    private VaultGroup? _renamingGroup;

    public VaultTreeView()
    {
        InitializeComponent();
    }

    private void AddGroupButton_Click(object sender, RoutedEventArgs e) => ShowAddGroupInput();
    private void AddChildGroupMenu_Click(object sender, RoutedEventArgs e) => ShowAddGroupInput();

    private void ShowAddGroupInput()
    {
        _renamingGroup = null;
        AddGroupButton.Visibility = Visibility.Collapsed;
        AddGroupPanel.Visibility = Visibility.Visible;
        GroupNameInput.Clear();
        GroupNameInput.Focus();
    }

    private void CancelAddGroup_Click(object sender, RoutedEventArgs e) => HideAddGroupInput();

    private void ConfirmAddGroup_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is VaultListViewModel vm && !string.IsNullOrWhiteSpace(GroupNameInput.Text))
        {
            string value = GroupNameInput.Text.Trim();
            if (_renamingGroup != null && vm.RenameGroupCommand.CanExecute(value))
                vm.RenameGroupCommand.Execute(value);
            else if (_renamingGroup == null && vm.AddGroupCommand.CanExecute(value))
                vm.AddGroupCommand.Execute(value);
        }
        HideAddGroupInput();
    }

    private void GroupNameInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            ConfirmAddGroup_Click(sender, e);
        else if (e.Key == Key.Escape)
            HideAddGroupInput();
    }

    private void HideAddGroupInput()
    {
        AddGroupPanel.Visibility = Visibility.Collapsed;
        AddGroupButton.Visibility = Visibility.Visible;
        GroupNameInput.Clear();
        _renamingGroup = null;
    }

    private void GroupsTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is VaultListViewModel vm && e.NewValue is VaultGroup group &&
            vm.SelectGroupCommand.CanExecute(group))
            vm.SelectGroupCommand.Execute(group);
    }

    private void DeleteGroupMenu_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is VaultListViewModel vm && GroupsTree.SelectedItem is VaultGroup group &&
            MessageBox.Show(
                L.F("删除分组“{0}”？其中的条目会保留在全部条目中。", group.Name),
                L.T("删除分组"),
                MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes &&
            vm.DeleteGroupCommand.CanExecute(group))
            vm.DeleteGroupCommand.Execute(group);
    }

    private void RenameGroupMenu_Click(object sender, RoutedEventArgs e)
    {
        if (GroupsTree.SelectedItem is not VaultGroup group)
            return;

        _renamingGroup = group;
        AddGroupButton.Visibility = Visibility.Collapsed;
        AddGroupPanel.Visibility = Visibility.Visible;
        GroupNameInput.Text = group.Name;
        GroupNameInput.Focus();
        GroupNameInput.SelectAll();
    }
}
