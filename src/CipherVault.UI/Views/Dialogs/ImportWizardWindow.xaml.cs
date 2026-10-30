namespace CipherVault.UI.Views.Dialogs;

using System.Windows;
using CipherVault.Core.Interfaces;
using CipherVault.UI.ViewModels;

public partial class ImportWizardWindow : Window
{
    private readonly ImportViewModel _viewModel;

    public ImportWizardWindow(ImportViewModel viewModel)
    {
        InitializeComponent();
        CipherVault.UI.Localization.LocalizationManager.Instance.TrackWindow(this);
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = _viewModel;

        _viewModel.ImportCompleted += OnImportCompleted;
        _viewModel.Cancelled += OnCancelled;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnImportCompleted(object? sender, ImportResult e)
    {
        // Result panel is shown via binding; just update DialogResult when user clicks Finish
    }

    private void OnCancelled(object? sender, EventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // When import finishes, set DialogResult
        if (e.PropertyName == nameof(ImportViewModel.IsImportFinished) && _viewModel.IsImportFinished)
        {
            // Don't auto-close; let user see result and click Finish
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void FinishButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void DropZone_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.BrowseFileCommand.Execute(null);
    }

    private void DropZone_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void DropZone_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop) &&
            e.Data.GetData(DataFormats.FileDrop) is string[] files &&
            files.Length > 0)
        {
            _viewModel.SetFilePathCommand.Execute(files[0]);
        }
    }
}
