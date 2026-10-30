namespace CipherVault.UI.Tests.Config;

using System.IO;
using System.Text.Json;
using CipherVault.App.Config;
using CipherVault.Core.Config;
using CipherVault.Core.Enums;
using CipherVault.UI.Themes;
using CipherVault.UI.ViewModels;
using FluentAssertions;
using Xunit;

public sealed class ConfigServiceTests
{
    [Fact]
    public void NewConfiguration_DisablesAntiCaptureByDefault()
    {
        var config = new AppConfig();

        config.Settings.Security.AntiCapture.Should().BeFalse();
        config.Settings.Tray.CloseBehavior.Should().Be("ask");
    }

    [Fact]
    public async Task SettingsSavePersistsCloseBehaviorAndKeepsTrayIconAvailable()
    {
        using var temp = new TempDirectory();
        string path = Path.Combine(temp.Path, "config.json");
        var service = new ConfigService(path);
        var sut = new SettingsViewModel(service)
        {
            CloseBehavior = "tray",
            ShowTrayIcon = false,
        };

        await sut.SaveCommand.ExecuteAsync(null);

        TraySettings saved = service.Load().Settings.Tray;
        saved.CloseBehavior.Should().Be("tray");
        saved.MinimizeToTray.Should().BeTrue();
        saved.ShowTrayIcon.Should().BeTrue();
    }

    [Fact]
    public async Task ConfigNormalizesUnknownCloseBehaviorToAsk()
    {
        using var temp = new TempDirectory();
        string path = Path.Combine(temp.Path, "config.json");
        var service = new ConfigService(path);
        var config = new AppConfig();
        config.Settings.Tray.CloseBehavior = "unexpected";
        config.Settings.Tray.MinimizeToTray = true;

        await service.SaveAsync(config);

        TraySettings saved = service.Load().Settings.Tray;
        saved.CloseBehavior.Should().Be("ask");
        saved.MinimizeToTray.Should().BeFalse();
    }

    [Fact]
    public async Task MissingAntiCaptureSetting_DefaultsToDisabled()
    {
        using var temp = new TempDirectory();
        string path = Path.Combine(temp.Path, "config.json");
        await File.WriteAllTextAsync(path, """
            {
              "settings": {
                "security": {
                  "idleTimeoutMinutes": 5
                }
              }
            }
            """);

        AppConfig loaded = new ConfigService(path).Load();

        loaded.Settings.Security.AntiCapture.Should().BeFalse();
    }

    [Fact]
    public async Task ResetToDefaults_DisablesAntiCapture()
    {
        using var temp = new TempDirectory();
        string path = Path.Combine(temp.Path, "config.json");
        var service = new ConfigService(path);
        var config = new AppConfig();
        config.Settings.Security.AntiCapture = true;
        await service.SaveAsync(config);
        var sut = new SettingsViewModel(service);

        sut.ResetToDefaultsCommand.Execute(null);

        sut.AntiCaptureEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task SavePersistsWindowsHelloAndRequestsDialogClose()
    {
        using var temp = new TempDirectory();
        string path = Path.Combine(temp.Path, "config.json");
        var service = new ConfigService(path);
        var sut = new SettingsViewModel(service);
        bool saved = false;
        bool closed = false;
        sut.SettingsSaved += (_, _) => saved = true;
        sut.CloseRequested += (_, _) => closed = true;
        sut.WindowsHelloEnabled = true;

        await sut.SaveCommand.ExecuteAsync(null);

        service.Load().Settings.Security.WindowsHelloEnabled.Should().BeTrue();
        saved.Should().BeTrue();
        closed.Should().BeTrue();
    }

    [Fact]
    public async Task CloseRequestsDialogCloseWithoutPersistingChanges()
    {
        using var temp = new TempDirectory();
        string path = Path.Combine(temp.Path, "config.json");
        var service = new ConfigService(path);
        await service.SaveAsync(new AppConfig());
        var sut = new SettingsViewModel(service);
        bool closed = false;
        sut.CloseRequested += (_, _) => closed = true;
        sut.AntiCaptureEnabled = true;

        sut.CloseCommand.Execute(null);

        closed.Should().BeTrue();
        service.Load().Settings.Security.AntiCapture.Should().BeFalse();
    }

    [Fact]
    public async Task ThemeSelection_PreviewsPersistsAndRevertsUnsavedChanges()
    {
        using var temp = new TempDirectory();
        string path = Path.Combine(temp.Path, "config.json");
        var service = new ConfigService(path);
        var initial = new AppConfig();
        initial.Settings.Appearance.Theme = "light";
        await service.SaveAsync(initial);
        var sut = new SettingsViewModel(service);

        sut.SelectedTheme = ThemeMode.Dark;
        ThemeManager.Instance.CurrentMode.Should().Be(ThemeMode.Dark);
        ThemeManager.Instance.IsDarkMode.Should().BeTrue();

        await sut.SaveCommand.ExecuteAsync(null);
        service.Load().Settings.Appearance.Theme.Should().Be("dark");

        sut.SelectedTheme = ThemeMode.Light;
        sut.CloseCommand.Execute(null);
        ThemeManager.Instance.CurrentMode.Should().Be(ThemeMode.Dark);
    }

    [Fact]
    public async Task SaveIsAtomicAndNormalizesUnsafeOrOutOfRangeValues()
    {
        using var temp = new TempDirectory();
        string path = Path.Combine(temp.Path, "config.json");
        var sut = new ConfigService(path);
        var config = new AppConfig
        {
            Vaults =
            [
                new AppConfigVault { Name = "A", Path = "a.cvault" },
                new AppConfigVault { Name = "duplicate", Path = "A.cvault" },
            ],
        };
        config.Settings.Security.WindowsHelloEnabled = true;
        config.Settings.Security.IdleTimeoutMinutes = 0;
        config.Settings.Security.ClipboardClearSeconds = 999;
        config.Settings.Appearance.SidebarWidth = 10;
        config.Settings.Appearance.FontSize = "huge";

        await sut.SaveAsync(config);

        File.Exists(path).Should().BeTrue();
        Directory.EnumerateFiles(temp.Path, "*.tmp").Should().BeEmpty();
        using JsonDocument json = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        json.RootElement.GetProperty("vaults").GetArrayLength().Should().Be(1);
        AppConfig loaded = new ConfigService(path).Load();
        loaded.Settings.Security.WindowsHelloEnabled.Should().BeTrue();
        loaded.Settings.Security.IdleTimeoutMinutes.Should().Be(1);
        loaded.Settings.Security.ClipboardClearSeconds.Should().Be(60);
        loaded.Settings.Appearance.SidebarWidth.Should().Be(160);
        loaded.Settings.Appearance.FontSize.Should().Be("medium");
    }

    [Fact]
    public async Task CorruptConfigIsPreservedBeforeReturningDefaults()
    {
        using var temp = new TempDirectory();
        string path = Path.Combine(temp.Path, "config.json");
        await File.WriteAllTextAsync(path, "{not-json");

        AppConfig loaded = new ConfigService(path).Load();

        loaded.Vaults.Should().BeEmpty();
        Directory.EnumerateFiles(temp.Path, "config.corrupt.*.json").Should().ContainSingle();
        (await File.ReadAllTextAsync(Directory.EnumerateFiles(temp.Path, "config.corrupt.*.json").Single()))
            .Should().Be("{not-json");
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "CipherVault.ConfigTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch { /* isolated test cleanup */ }
        }
    }
}
