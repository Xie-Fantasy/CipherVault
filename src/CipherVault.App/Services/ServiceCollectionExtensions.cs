namespace CipherVault.App.Services;

using CipherVault.App.Config;
using CipherVault.Core.Interfaces;
using CipherVault.Infrastructure;
using CipherVault.Infrastructure.Backup;
using CipherVault.Infrastructure.Crypto;
using CipherVault.Infrastructure.Export;
using CipherVault.Infrastructure.Import;
using CipherVault.Infrastructure.Security;
using CipherVault.Infrastructure.Vault;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for registering all CipherVault services into the DI container.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCipherVaultServices(this IServiceCollection services)
    {
        // --- Application configuration (Singleton) ---
        services.AddSingleton<IConfigService, ConfigService>();

        // --- Core infrastructure (Singleton) ---
        services.AddSingleton<IEventBus, EventBus>();
        services.AddSingleton<ICryptoService, CryptoService>();
        services.AddSingleton<ISecureMemoryManager, SecureMemoryManager>();
        services.AddSingleton<IAntiCaptureService, AntiCaptureService>();
        services.AddSingleton<IGlobalHotkeyService, GlobalHotkeyService>();
        services.AddSingleton<IWindowsHelloService, WindowsHelloService>();

        // VaultKeyManager depends on ISecureMemoryManager
        services.AddSingleton<VaultKeyManager>();

        // EncryptedVaultFileManager depends on ICryptoService
        services.AddSingleton<EncryptedVaultFileManager>();

        // BruteForceGuard is stateless utility, singleton is fine
        services.AddSingleton<BruteForceGuard>();

        // RecoveryKeyGenerator
        services.AddSingleton<RecoveryKeyGenerator>();

        // BackupService
        services.AddSingleton<IBackupService, BackupService>();

        // BackupScheduler depends on IBackupService
        services.AddSingleton<BackupScheduler>();

        // ExportService provides vault data export in multiple formats
        services.AddSingleton<IExportService, ExportService>();

        // --- Scoped / Transient services ---

        // ClipboardGuard needs clipboard access delegates, registered as factory
        services.AddSingleton<IClipboardGuard>(sp =>
        {
            return new ClipboardGuard(
                setClipboard: text =>
                {
                    if (System.Windows.Application.Current?.MainWindow is { } window)
                    {
                        window.Dispatcher.Invoke(() =>
                        {
                            try { System.Windows.Clipboard.SetText(text); }
                            catch { /* clipboard access can fail if locked by another app */ }
                        });
                    }
                },
                clearClipboard: () =>
                {
                    if (System.Windows.Application.Current?.MainWindow is { } window)
                    {
                        window.Dispatcher.Invoke(() =>
                        {
                            try
                            {
                                // Overwrite with space before clearing to defeat clipboard managers
                                System.Windows.Clipboard.SetText(" ");
                                System.Windows.Clipboard.Clear();
                            }
                            catch { /* clipboard access can fail */ }
                        });
                    }
                },
                getClipboard: () =>
                {
                    if (System.Windows.Application.Current?.MainWindow is not { } window)
                        return null;

                    return window.Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            return System.Windows.Clipboard.ContainsText()
                                ? System.Windows.Clipboard.GetText()
                                : null;
                        }
                        catch
                        {
                            return null;
                        }
                    });
                }
            );
        });

        // ImportService depends on IVaultRepository (registered later when vault is opened)
        services.AddTransient<CsvImportService>();

        return services;
    }
}
