namespace CipherVault.UI.Views.Popups;

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using CipherVault.UI.ViewModels;

public partial class GlobalSearchPopup : Window
{
    [DllImport("user32.dll")]
    private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint dwAffinity);

    private const uint WDA_EXCLUDEFROMCAPTURE = 0x11;

    public GlobalSearchPopup()
    {
        InitializeComponent();
        CipherVault.UI.Localization.LocalizationManager.Instance.TrackWindow(this);
        Loaded += GlobalSearchPopup_Loaded;
        Deactivated += GlobalSearchPopup_Deactivated;
    }

    private void GlobalSearchPopup_Loaded(object sender, RoutedEventArgs e)
    {
        // Apply anti-capture (set window display affinity)
        var hwnd = new WindowInteropHelper(this).Handle;
        SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE);

        // Focus the search input
        SearchInput.Focus();
    }

    private void GlobalSearchPopup_Deactivated(object? sender, EventArgs e)
    {
        // Auto-close when window loses focus
        Close();
    }

    private void SearchInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not GlobalSearchViewModel vm)
            return;

        switch (e.Key)
        {
            case Key.Escape:
                Close();
                break;

            case Key.Tab:
                // Switch between username/password copy mode
                vm.SwitchCopyModeCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Enter:
                // Execute copy
                if (vm.ExecuteCopyCommand.CanExecute(null))
                {
                    vm.ExecuteCopyCommand.Execute(null);
                    Close();
                }
                break;

            case Key.Down:
                // Move selection down in results
                MoveSelection(1);
                break;

            case Key.Up:
                // Move selection up in results
                MoveSelection(-1);
                break;
        }
    }

    private void MoveSelection(int direction)
    {
        if (DataContext is GlobalSearchViewModel vm && vm.Results.Count > 0)
        {
            var currentIndex = vm.SelectedEntry != null
                ? vm.Results.IndexOf(vm.SelectedEntry)
                : -1;
            var newIndex = currentIndex + direction;

            if (newIndex >= 0 && newIndex < vm.Results.Count)
            {
                vm.SelectedEntry = vm.Results[newIndex];
            }
            else if (newIndex < 0)
            {
                vm.SelectedEntry = vm.Results[^1];
            }
            else
            {
                vm.SelectedEntry = vm.Results[0];
            }
        }
    }

    /// <summary>
    /// Positions the popup centered on the screen containing the mouse cursor.
    /// </summary>
    public void PositionAtMouseScreen()
    {
        var screen = System.Windows.Forms.Screen.FromPoint(
            System.Windows.Forms.Cursor.Position);

        var workingArea = screen.WorkingArea;
        Left = workingArea.Left + (workingArea.Width - ActualWidth) / 2;
        Top = workingArea.Top + (workingArea.Height - ActualHeight) / 2;
    }

    /// <summary>
    /// Positions the popup centered on the primary screen.
    /// </summary>
    public void PositionCenterScreen()
    {
        var screen = System.Windows.Forms.Screen.PrimaryScreen;
        if (screen != null)
        {
            var workingArea = screen.WorkingArea;
            Left = workingArea.Left + (workingArea.Width - ActualWidth) / 2;
            Top = workingArea.Top + (workingArea.Height - ActualHeight) / 2;
        }
    }

    /// <summary>
    /// Clears the search and resets the popup state.
    /// </summary>
    public void Reset()
    {
        if (DataContext is GlobalSearchViewModel vm)
        {
            vm.SearchQuery = string.Empty;
        }
        SearchInput.Clear();
        SearchInput.Focus();
    }
}
