using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using CipherVault.App.Services;
using CipherVault.Infrastructure.Vault;
using CipherVault.UI.Themes;
using CipherVault.UI.ViewModels;
using CipherVault.UI.Views;
using CipherVault.UI.Localization;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using MessageBox = System.Windows.MessageBox;

namespace CipherVault.App;

/// <summary>
/// Application entry point. Initializes DI container, logging, and theme system.
/// </summary>
public partial class App : System.Windows.Application
{
    private ServiceProvider? _serviceProvider;
    private AppCoordinator? _coordinator;
    private Mutex? _singleInstanceMutex;

    /// <summary>
    /// The application-wide service provider. Available after OnStartup completes.
    /// </summary>
    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        // The UI is dominated by text and simple vector geometry. On common Intel
        // drivers WPF hardware rendering can reserve hundreds of MiB for D3D/media
        // modules, while this application gets no meaningful visual benefit from it.
        // Software rendering keeps the resident/private footprint predictable.
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

        base.OnStartup(e);

        LocalizationManager.Instance.CurrentLanguage =
            System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en"
                ? LocalizationManager.English
                : LocalizationManager.SimplifiedChinese;

        _singleInstanceMutex = new Mutex(true, "Local\\CipherVault.SingleInstance", out bool isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show(L.T("CipherVault 已在运行。"), "CipherVault",
                MessageBoxButton.OK, MessageBoxImage.Information);
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            Shutdown();
            return;
        }

        EncryptedVaultFileManager.CleanupOrphanedSessionFiles();

        // --- 1. Initialize Serilog ---
        var logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CipherVault", "logs");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Debug()
            .WriteTo.File(
                Path.Combine(logDir, "ciphervault-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        ViewModelBase.ErrorReported += OnViewModelErrorReported;
        Log.Information("CipherVault starting up");

        // --- 2. Initialize DI container ---
        var services = new ServiceCollection();
        services.AddCipherVaultServices();

        try
        {
            _serviceProvider = services.BuildServiceProvider();
            Services = _serviceProvider;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Failed to build service provider");
            MessageBox.Show(
                "Application initialization failed. Please check the log files for details.",
                "CipherVault - Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        // --- 3. Initialize theme system ---
        try
        {
            ThemeManager.Instance.ApplyTheme();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to apply initial theme, continuing with defaults");
        }

        // --- 4. Start application coordinator ---
        try
        {
            _coordinator = new AppCoordinator(_serviceProvider);
            _coordinator.Start();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Failed to start application coordinator");
            MessageBox.Show(
                "Application startup failed. Please check the log files for details.",
                "CipherVault - Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        Log.Information("CipherVault started successfully");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("CipherVault shutting down");
        ViewModelBase.ErrorReported -= OnViewModelErrorReported;

        _coordinator?.Dispose();
        _coordinator = null;

        // Dispose managed services
        if (_serviceProvider is IDisposable disposable)
        {
            disposable.Dispose();
        }

        Log.CloseAndFlush();
        if (_singleInstanceMutex != null)
        {
            try { _singleInstanceMutex.ReleaseMutex(); }
            catch (ApplicationException) { }
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
        }
        base.OnExit(e);
    }

    private static void OnViewModelErrorReported(object? sender, ViewModelErrorEventArgs e)
    {
        Log.Error(
            e.Exception,
            "View model {ViewModel} failed while {ErrorContext}",
            e.ViewModelName,
            e.ErrorContext);
    }
}
