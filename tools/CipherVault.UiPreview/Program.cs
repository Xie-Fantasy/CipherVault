using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CipherVault.Core.Models;
using CipherVault.Infrastructure.Database;
using CipherVault.Infrastructure.Vault;
using CipherVault.UI.Localization;
using CipherVault.UI.ViewModels;
using CipherVault.UI.Views.Controls;
using Microsoft.Data.Sqlite;

namespace CipherVault.UiPreview;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        string outputDirectory = args.Length > 0
            ? Path.GetFullPath(args[0])
            : Path.Combine(Path.GetTempPath(), "CipherVault-UI-Preview");
        Directory.CreateDirectory(outputDirectory);

        var application = new Application
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown,
        };
        AddResource("Colors.xaml");
        AddResource("Typography.xaml");
        AddResource("ControlStyles.xaml");

        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        SchemaMigrator.InitializeSchema(connection);
        using var repository = new VaultRepository(connection);

        DateTime baseline = new(2026, 7, 27, 8, 0, 0, DateTimeKind.Utc);
        var entries = new[]
        {
            new VaultEntry { Title = "GitHub", Username = "developer@example.com", CreatedAt = baseline, ModifiedAt = baseline.AddHours(3), IsFavorite = true },
            new VaultEntry { Title = "招商银行", Username = "尾号 6688", CreatedAt = baseline.AddHours(1), ModifiedAt = baseline.AddHours(2) },
            new VaultEntry { Title = "Steam", Username = "cipher_player", CreatedAt = baseline.AddHours(2), ModifiedAt = baseline.AddHours(1) },
            new VaultEntry { Title = "家庭 Wi-Fi", Username = "Home-5G", CreatedAt = baseline.AddHours(3), ModifiedAt = baseline },
        };
        foreach (VaultEntry entry in entries)
            repository.InsertEntryAsync(entry).GetAwaiter().GetResult();
        repository.InsertGroupAsync(new VaultGroup { Name = "工作" }).GetAwaiter().GetResult();
        repository.InsertGroupAsync(new VaultGroup { Name = "金融" }).GetAwaiter().GetResult();
        repository.InsertTagAsync(new Tag { Name = "主账号" }).GetAwaiter().GetResult();
        repository.InsertTagAsync(new Tag { Name = "重要" }).GetAwaiter().GetResult();

        var viewModel = new EntryListViewModel(repository);
        viewModel.LoadSpecialViewAsync("All").GetAwaiter().GetResult();
        viewModel.IsBulkMode = true;

        var control = new EntryListControl
        {
            DataContext = viewModel,
            Width = 500,
            Height = 760,
        };
        var window = new Window
        {
            Content = control,
            Width = 500,
            Height = 760,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            ShowActivated = false,
            Left = -10000,
            Top = -10000,
            Background = (Brush)application.Resources["BrushBackground"],
        };
        window.Show();
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

        var list = (ListView)control.FindName("EntriesList");
        list.SelectedItems.Add(viewModel.Entries[0]);
        list.SelectedItems.Add(viewModel.Entries[1]);
        list.SelectedItems.Add(viewModel.Entries[2]);
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        Render(control, Path.Combine(outputDirectory, "entry-list-bulk.png"));

        viewModel.SortMode = EntrySortMode.ModifiedDescending;
        var popup = (Popup)control.FindName("SortPopup");
        popup.IsOpen = true;
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        if (popup.Child is FrameworkElement popupContent)
        {
            popupContent.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            popupContent.Arrange(new Rect(popupContent.DesiredSize));
            popupContent.UpdateLayout();
            Render(popupContent, Path.Combine(outputDirectory, "entry-list-sort-popup.png"));
        }

        popup.IsOpen = false;
        LocalizationManager.Instance.CurrentLanguage = LocalizationManager.English;
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        Render(control, Path.Combine(outputDirectory, "entry-list-bulk-en.png"));

        popup.IsOpen = true;
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        if (popup.Child is FrameworkElement englishPopupContent)
        {
            englishPopupContent.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            englishPopupContent.Arrange(new Rect(englishPopupContent.DesiredSize));
            englishPopupContent.UpdateLayout();
            Render(englishPopupContent, Path.Combine(outputDirectory, "entry-list-sort-popup-en.png"));
        }

        popup.IsOpen = false;
        window.Close();
        application.Shutdown();
    }

    private static void AddResource(string fileName)
    {
        Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(
                $"pack://application:,,,/CipherVault.UI;component/Themes/{fileName}",
                UriKind.Absolute),
        });
    }

    private static void Render(FrameworkElement element, string path)
    {
        int width = Math.Max(1, (int)Math.Ceiling(element.ActualWidth));
        int height = Math.Max(1, (int)Math.Ceiling(element.ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
