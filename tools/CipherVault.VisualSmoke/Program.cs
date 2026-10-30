using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CipherVault.Core.Config;
using CipherVault.Core.Enums;
using CipherVault.Core.Interfaces;
using CipherVault.Core.Models;
using CipherVault.UI.ViewModels;
using CipherVault.UI.Localization;
using CipherVault.UI.Themes;
using CipherVault.UI.Views;
using CipherVault.UI.Views.Controls;
using CipherVault.UI.Views.Dialogs;

namespace CipherVault.VisualSmoke;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        string outputDirectory = args.Length > 0
            ? Path.GetFullPath(args[0])
            : Path.Combine(Path.GetTempPath(), "CipherVaultVisualSmoke");
        Directory.CreateDirectory(outputDirectory);

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        AddResources(app, "Colors.xaml");
        LocalizationManager.Instance.CurrentLanguage = LocalizationManager.SimplifiedChinese;

        VerifyMainSearchBinding();
        Render(Path.Combine(outputDirectory, "main-light.png"), locked: false);
        Render(Path.Combine(outputDirectory, "main-bulk-light.png"), locked: false, bulkMode: true);
        ThemeManager.Instance.ApplyFontSize("large");
        Render(Path.Combine(outputDirectory, "main-large-light.png"), locked: false);
        ThemeManager.Instance.ApplyFontSize("medium");
        RenderSetup(Path.Combine(outputDirectory, "setup-location-light.png"), currentStep: 2);
        RenderSetup(Path.Combine(outputDirectory, "setup-recovery-light.png"), currentStep: 4);
        RenderSettings(Path.Combine(outputDirectory, "settings-appearance-light.png"), ThemeMode.Light);
        RenderSettings(Path.Combine(outputDirectory, "settings-security-light.png"), ThemeMode.Light, showSecurity: true);
        RenderSettings(Path.Combine(outputDirectory, "settings-tray-light.png"), ThemeMode.Light, showTray: true);
        RenderCloseBehavior(Path.Combine(outputDirectory, "close-behavior-light.png"));
        RenderEntryEdit(Path.Combine(outputDirectory, "entry-edit-chinese-light.png"));
        ReplaceColors(app, "DarkColors.xaml");
        Render(Path.Combine(outputDirectory, "main-dark.png"), locked: false);
        Render(Path.Combine(outputDirectory, "locked-dark.png"), locked: true);
        RenderSetup(Path.Combine(outputDirectory, "setup-location-dark.png"), currentStep: 2);
        RenderSetup(Path.Combine(outputDirectory, "setup-recovery-dark.png"), currentStep: 4);
        RenderSettings(Path.Combine(outputDirectory, "settings-appearance-dark.png"), ThemeMode.Dark);
        RenderSettings(Path.Combine(outputDirectory, "settings-security-dark.png"), ThemeMode.Dark, showSecurity: true);
        RenderExport(Path.Combine(outputDirectory, "export-dark.png"));
        ReplaceColors(app, "Colors.xaml");
        LocalizationManager.Instance.CurrentLanguage = LocalizationManager.English;
        Render(Path.Combine(outputDirectory, "main-english-light.png"), locked: false);
        Render(Path.Combine(outputDirectory, "main-bulk-english-light.png"), locked: false, bulkMode: true);
        RenderSettings(
            Path.Combine(outputDirectory, "settings-appearance-english-light.png"),
            ThemeMode.Light);
        RenderSettings(
            Path.Combine(outputDirectory, "settings-tray-english-light.png"),
            ThemeMode.Light,
            showTray: true);
        RenderCloseBehavior(Path.Combine(outputDirectory, "close-behavior-english-light.png"));
        RenderEntryEdit(Path.Combine(outputDirectory, "entry-edit-english-light.png"));
        ThemeManager.Instance.ApplyFontSize("large");
        RenderSettings(
            Path.Combine(outputDirectory, "settings-security-english-large-light.png"),
            ThemeMode.Light,
            showSecurity: true);
        ThemeManager.Instance.ApplyFontSize("medium");
        RenderLiveLanguageSwitch(
            Path.Combine(outputDirectory, "settings-live-language-switch.png"));
        LocalizationManager.Instance.CurrentLanguage = LocalizationManager.English;
        RenderExport(Path.Combine(outputDirectory, "export-english-light.png"));
        app.Shutdown();
        return 0;
    }

    private static void AddResources(Application app, string colors)
    {
        app.Resources.MergedDictionaries.Add(Dictionary(colors));
        app.Resources.MergedDictionaries.Add(Dictionary("Typography.xaml"));
        app.Resources.MergedDictionaries.Add(Dictionary("ControlStyles.xaml"));
    }

    private static void ReplaceColors(Application app, string colors)
    {
        ThemeManager.Instance.CurrentMode = colors.Equals("DarkColors.xaml", StringComparison.OrdinalIgnoreCase)
            ? ThemeMode.Dark
            : ThemeMode.Light;
    }

    private static ResourceDictionary Dictionary(string name) => new()
    {
        Source = new Uri($"pack://application:,,,/CipherVault.UI;component/Themes/{name}", UriKind.Absolute),
    };

    private static void Render(string path, bool locked, bool bulkMode = false)
    {
        PreviewMainViewModel viewModel = PreviewMainViewModel.Create(locked);
        if (bulkMode)
        {
            viewModel.EntryListVM.HasSelection = true;
            viewModel.EntryListVM.SelectedCount = viewModel.EntryListVM.Entries.Count;
            viewModel.EntryListVM.AvailableGroups =
            [
                new BulkGroupOption(null, LocalizationManager.Instance.Translate("未分组")),
                new BulkGroupOption("work", LocalizationManager.Instance.Translate("工作")),
            ];
            viewModel.EntryListVM.AvailableTags =
            [
                new Tag { Name = LocalizationManager.Instance.Translate("开发") },
            ];
            viewModel.EntryListVM.SelectedBulkGroup = viewModel.EntryListVM.AvailableGroups[0];
            viewModel.EntryListVM.SelectedBulkTag = viewModel.EntryListVM.AvailableTags[0];
        }

        var window = new MainWindow
        {
            DataContext = viewModel,
            Width = 1100,
            Height = 720,
        };
        var content = (FrameworkElement)window.Content;
        LocalizationManager.Instance.ApplyToElement(window);
        content.Measure(new Size(1100, 720));
        content.Arrange(new Rect(0, 0, 1100, 720));
        content.UpdateLayout();

        var bitmap = new RenderTargetBitmap(1100, 720, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
        window.Close();
    }

    private static void VerifyMainSearchBinding()
    {
        var viewModel = PreviewMainViewModel.Create(locked: false);
        var window = new MainWindow
        {
            DataContext = viewModel,
            Left = -10000,
            Top = -10000,
            ShowInTaskbar = false,
        };
        window.Show();
        var searchControl = (SearchBarControl)window.FindName("MainSearchBar");
        var searchBox = (System.Windows.Controls.TextBox)searchControl.FindName("SearchBox");

        searchBox.Text = "binding-check";
        searchBox.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();
        window.Dispatcher.Invoke(
            () => { },
            System.Windows.Threading.DispatcherPriority.DataBind);
        searchControl.GetBindingExpression(SearchBarControl.SearchQueryProperty)?.UpdateSource();

        if (viewModel.EntryListVM.SearchQuery != "binding-check")
            throw new InvalidOperationException("The main search box is not bound to EntryListVM.SearchQuery.");

        window.Close();
    }

    private static void RenderSetup(string path, int currentStep)
    {
        var window = new SetupWizardWindow
        {
            DataContext = PreviewSetupViewModel.Create(currentStep),
            Width = 640,
            Height = 600,
        };
        var content = (FrameworkElement)window.Content;
        LocalizationManager.Instance.ApplyToElement(window);
        content.Measure(new Size(640, 600));
        content.Arrange(new Rect(0, 0, 640, 600));
        content.UpdateLayout();

        var bitmap = new RenderTargetBitmap(640, 600, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
        window.Close();
    }

    private static void RenderSettings(
        string path,
        ThemeMode selectedTheme,
        bool showSecurity = false,
        bool showTray = false)
    {
        const int width = 900;
        const int height = 680;
        var config = new AppConfig();
        config.Settings.Appearance.Theme = selectedTheme.ToString().ToLowerInvariant();
        config.Settings.Appearance.Language = LocalizationManager.Instance.CurrentLanguage;
        var viewModel = new SettingsViewModel(new PreviewConfigService(config));
        var window = new SettingsWindow(viewModel) { Width = width, Height = height };
        string tabName = showTray
            ? "TrayTab"
            : showSecurity
                ? "SecurityTab"
                : "AppearanceTab";
        ((System.Windows.Controls.RadioButton)window.FindName(tabName)).IsChecked = true;

        var content = (FrameworkElement)window.Content;
        LocalizationManager.Instance.ApplyToElement(window);
        content.Measure(new Size(width, height));
        content.Arrange(new Rect(0, 0, width, height));
        content.UpdateLayout();

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
        viewModel.CloseCommand.Execute(null);
    }

    private static void RenderCloseBehavior(string path)
    {
        const int width = 600;
        const int height = 450;
        var window = new CloseBehaviorDialog
        {
            Width = width,
            Height = height,
        };
        var content = (FrameworkElement)window.Content;
        LocalizationManager.Instance.ApplyToElement(window);
        content.Measure(new Size(width, height));
        content.Arrange(new Rect(0, 0, width, height));
        content.UpdateLayout();

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
        window.Close();
    }

    private static void RenderExport(string path)
    {
        var window = new ExportDialog
        {
            DataContext = new PreviewExportViewModel(),
            Width = 500,
            Height = 440,
        };
        var content = (FrameworkElement)window.Content;
        LocalizationManager.Instance.ApplyToElement(window);
        content.Measure(new Size(500, 440));
        content.Arrange(new Rect(0, 0, 500, 440));
        content.UpdateLayout();

        var bitmap = new RenderTargetBitmap(500, 440, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
        window.Close();
    }

    private static void RenderEntryEdit(string path)
    {
        const int width = 720;
        const int height = 650;
        var entry = new VaultEntry
        {
            Title = "Microsoft account",
            Url = "https://account.microsoft.com",
        };
        var control = new EntryDetailPanel
        {
            Width = width,
            Height = height,
            DataContext = new PreviewEntryDetail
            {
                CurrentEntry = entry,
                IsEditing = true,
                EditTitle = entry.Title,
                EditUrl = entry.Url,
                EditCredentialType = CredentialType.Custom,
                CredentialTypes = Enum.GetValues<CredentialType>(),
            },
        };

        LocalizationManager.Instance.ApplyToElement(control);
        control.Measure(new Size(width, height));
        control.Arrange(new Rect(0, 0, width, height));
        control.UpdateLayout();

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(control);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static void RenderLiveLanguageSwitch(string path)
    {
        LocalizationManager.Instance.CurrentLanguage = LocalizationManager.SimplifiedChinese;
        var config = new AppConfig();
        config.Settings.Appearance.Language = LocalizationManager.SimplifiedChinese;
        var viewModel = new SettingsViewModel(new PreviewConfigService(config));
        var window = new SettingsWindow(viewModel)
        {
            Width = 900,
            Height = 680,
            Left = -10000,
            Top = -10000,
            ShowInTaskbar = false,
        };
        ((System.Windows.Controls.RadioButton)window.FindName("AppearanceTab")).IsChecked = true;
        window.Show();

        viewModel.SelectedLanguage = LocalizationManager.English;
        window.Dispatcher.Invoke(
            () => { },
            System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(900, 680));
        content.Arrange(new Rect(0, 0, 900, 680));
        content.UpdateLayout();
        var bitmap = new RenderTargetBitmap(900, 680, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
        viewModel.CloseCommand.Execute(null);
    }
}

internal sealed class PreviewExportViewModel
{
    public ExportFormat SelectedFormat { get; set; } = ExportFormat.KeePassXml;
    public string WarningMessage => LocalizationManager.Instance.Translate(
        "警告：此格式将以明文形式包含所有密码。请勿直接用表格软件打开；请安全保存并在导入后及时删除。需要可恢复的加密副本时，请使用“立即备份”。");
    public string ExportFilePath { get; set; } = @"C:\Users\demo\Documents\vault-export.xml";
    public bool IsExporting => false;
    public string? ErrorMessage => null;
    public string? ExportResult => null;
}

internal sealed class PreviewConfigService(AppConfig config) : IConfigService
{
    public string ConfigFilePath => "preview-config.json";
    public bool IsFirstRun => false;
    public AppConfig Load() => config;
    public Task SaveAsync(AppConfig value) => Task.CompletedTask;
    public void AddVault(string name, string path) { }
    public void RemoveVault(string path) { }
    public string? GetLastOpenedVaultPath() => null;
    public void SetLastOpenedVault(string path) { }
}

internal sealed class PreviewSetupViewModel
{
    public int CurrentStep { get; init; } = 4;
    public string VaultName { get; set; } = "个人保险库";
    public string VaultFilePath { get; set; } = @"C:\Users\demo\Documents\CipherVault\个人保险库.cvault";
    public int PasswordStrength => 4;
    public string PasswordStrengthLabel => "强";
    public bool CanGoBack => true;
    public bool CanGoNext => CurrentStep < 4;
    public bool CanFinish => CurrentStep == 4;
    public bool IsCreating => false;
    public string? ErrorMessage => null;
    public int VerificationWordIndex => 6;
    public int VerificationWordNumber => VerificationWordIndex + 1;
    public string VerificationWordInput { get; set; } = "absorb";
    public IReadOnlyList<RecoveryWordDisplay> RecoveryKeyDisplay { get; init; } = [];

    public static PreviewSetupViewModel Create(int currentStep = 4)
    {
        string[] words =
        [
            "abandon", "ability", "able", "about", "above", "absent",
            "absorb", "abstract", "absurd", "abuse", "access", "accident"
        ];
        return new PreviewSetupViewModel
        {
            CurrentStep = currentStep,
            RecoveryKeyDisplay = words
                .Select((word, index) => new RecoveryWordDisplay(index + 1, word))
                .ToArray(),
        };
    }
}

internal sealed class PreviewMainViewModel
{
    public bool IsLocked { get; init; }
    public string GlobalSearchHotkey { get; init; } = "Ctrl+Shift+P";
    public GridLength SidebarWidth { get; init; } = new(220);
    public GridLength EntryListWidth { get; init; } = new(320);
    public string StatusMessage => IsLocked ? "已锁定" : "已解锁 · 6 个条目 · 刚刚备份";
    public int TotalEntries => 6;
    public DateTime? LastBackupTime => DateTime.Now.AddMinutes(-2);
    public PreviewVaultList VaultListVM { get; init; } = new();
    public PreviewEntryList EntryListVM { get; init; } = new();
    public PreviewEntryDetail EntryDetailVM { get; init; } = new();

    public static PreviewMainViewModel Create(bool locked)
    {
        var vault = new AppConfigVault { Name = "个人保险库", Path = "preview.cvault" };
        var github = new VaultEntry
        {
            Title = "GitHub",
            Username = "developer@example.com",
            Url = "https://github.com",
            Notes = "主要开发账号，已启用双重验证。",
            IsFavorite = true,
            CustomFields = [new CustomField { Key = "恢复邮箱", Value = "backup@example.com" }],
        };
        return new PreviewMainViewModel
        {
            IsLocked = locked,
            VaultListVM = new PreviewVaultList
            {
                Vaults = [vault],
                SelectedVault = vault,
                Groups =
                [
                    new VaultGroup { Name = "工作" },
                    new VaultGroup { Name = "金融" },
                    new VaultGroup { Name = "社交" },
                ],
            },
            EntryListVM = new PreviewEntryList
            {
                Entries =
                [
                    github,
                    new VaultEntry { Title = "Google", Username = "user@gmail.com", IsFavorite = true },
                    new VaultEntry { Title = "招商银行", Username = "尾号 6688" },
                    new VaultEntry { Title = "Steam", Username = "cipher_player" },
                    new VaultEntry { Title = "家用 Wi-Fi", Username = "Home-5G", CredentialType = CredentialType.WiFi },
                ],
            },
            EntryDetailVM = new PreviewEntryDetail
            {
                CurrentEntry = github,
                Tags = [new Tag { Name = "开发" }, new Tag { Name = "主账号" }],
            },
        };
    }
}

internal sealed class PreviewVaultList
{
    public ObservableCollection<AppConfigVault> Vaults { get; init; } = [];
    public AppConfigVault? SelectedVault { get; set; }
    public ObservableCollection<VaultGroup> Groups { get; init; } = [];
    public string SpecialView { get; set; } = "All";
}

internal sealed class PreviewEntryList
{
    public ObservableCollection<VaultEntry> Entries { get; init; } = [];
    public int FilteredCount => Entries.Count;
    public VaultEntry? SelectedEntry { get; set; }
    public string SearchQuery { get; set; } = string.Empty;
    public bool ShowFavoritesOnly { get; set; }
    public bool HasSelection { get; set; }
    public int SelectedCount { get; set; }
    public bool IsTrashView { get; set; }
    public ObservableCollection<BulkGroupOption> AvailableGroups { get; set; } = [];
    public ObservableCollection<Tag> AvailableTags { get; set; } = [];
    public BulkGroupOption? SelectedBulkGroup { get; set; }
    public Tag? SelectedBulkTag { get; set; }
}

internal sealed class PreviewEntryDetail
{
    public VaultEntry? CurrentEntry { get; init; }
    public bool IsEditing { get; init; }
    public bool ShowPassword { get; init; }
    public string? DecryptedPassword { get; init; }
    public bool IsHistoryVisible { get; init; }
    public bool IsCurrentEntryDeleted { get; init; }
    public ObservableCollection<Tag> Tags { get; init; } = [];
    public ObservableCollection<EntryHistory> HistoryEntries { get; init; } = [];
    public ObservableCollection<CustomField> EditCustomFields { get; init; } = [];
    public string EditTitle { get; init; } = string.Empty;
    public string? EditUrl { get; init; }
    public CredentialType EditCredentialType { get; init; }
    public IReadOnlyList<CredentialType> CredentialTypes { get; init; } = Enum.GetValues<CredentialType>();
    public string? ErrorMessage { get; init; }
}
