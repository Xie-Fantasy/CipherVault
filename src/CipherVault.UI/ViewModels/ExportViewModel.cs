namespace CipherVault.UI.ViewModels;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CipherVault.Core.Interfaces;
using CipherVault.Infrastructure.Crypto;
using CipherVault.UI.Localization;

/// <summary>
/// ViewModel for the vault export dialog.
/// Coordinates format selection, file path input, and export execution.
/// </summary>
public partial class ExportViewModel : ViewModelBase
{
    private readonly IExportService _exportService;
    private readonly IVaultRepository _repository;
    private readonly ICryptoService _cryptoService;
    private readonly VaultKeyManager _keyManager;

    /// <summary>
    /// Raised when the view should open a SaveFileDialog to pick an export file path.
    /// </summary>
    public event EventHandler<BrowseFileEventArgs>? BrowseFileRequested;

    /// <summary>
    /// Raised after a successful export, signaling the dialog to close.
    /// </summary>
    public event EventHandler? ExportCompleted;

    /// <summary>
    /// Raised when the user cancels the export dialog.
    /// </summary>
    public event EventHandler? Cancelled;

    public ExportViewModel(
        IExportService exportService,
        IVaultRepository repository,
        ICryptoService cryptoService,
        VaultKeyManager keyManager)
    {
        _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _cryptoService = cryptoService ?? throw new ArgumentNullException(nameof(cryptoService));
        _keyManager = keyManager ?? throw new ArgumentNullException(nameof(keyManager));
        WarningMessage = L.T(PlaintextExportWarning);
    }

    #region Properties

    /// <summary>
    /// The currently selected export format.
    /// </summary>
    [ObservableProperty]
    private ExportFormat _selectedFormat = ExportFormat.KeePassXml;

    /// <summary>
    /// The target file path for the export.
    /// </summary>
    [ObservableProperty]
    private string? _exportFilePath;

    /// <summary>
    /// Whether an export operation is currently in progress.
    /// </summary>
    [ObservableProperty]
    private bool _isExporting;

    /// <summary>
    /// Error message to display if export fails.
    /// </summary>
    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>
    /// Success message displayed after a successful export.
    /// </summary>
    [ObservableProperty]
    private string? _exportResult;

    /// <summary>
    /// Warning message shown when CSV format is selected (plaintext passwords).
    /// </summary>
    [ObservableProperty]
    private string? _warningMessage;

    /// <summary>
    /// Available export format descriptions for display in the UI.
    /// </summary>
    public List<FormatDescription> AvailableFormats { get; } = new()
    {
        new FormatDescription(ExportFormat.Csv, L.T("CSV 文件（明文密码）"), L.T("用于迁移到其他密码管理器，请勿直接用表格软件打开")),
        new FormatDescription(ExportFormat.KeePassXml, L.T("KeePass XML 格式（明文）"), L.T("兼容 KeePass 2.x，包含明文密码")),
    };

    #endregion

    #region Property Change Handlers

    partial void OnSelectedFormatChanged(ExportFormat value)
    {
        // Show/hide warning for CSV format
        WarningMessage = value is ExportFormat.Csv or ExportFormat.KeePassXml
            ? L.T(PlaintextExportWarning)
            : null;

        // Clear previous error/result when format changes
        ErrorMessage = null;
        ExportResult = null;
    }

    #endregion

    #region Commands

    /// <summary>
    /// Requests the view to open a SaveFileDialog for picking the export file path.
    /// </summary>
    [RelayCommand]
    private void BrowseFile()
    {
        BrowseFileRequested?.Invoke(this, new BrowseFileEventArgs(SelectedFormat));
    }

    /// <summary>
    /// Executes the export operation based on the selected format and file path.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportAsync()
    {
        if (string.IsNullOrWhiteSpace(ExportFilePath))
        {
            ErrorMessage = L.T("请选择导出文件路径。");
            return;
        }

        IsExporting = true;
        ErrorMessage = null;
        ExportResult = null;

        try
        {
            var entries = await _repository.GetEntriesAsync();
            var groups = await _repository.GetGroupsAsync();
            var tags = await _repository.GetTagsAsync();

            switch (SelectedFormat)
            {
                case ExportFormat.EncryptedJson:
                    await _keyManager.UseKeyAsync(key =>
                        _exportService.ExportEncryptedJsonAsync(
                            ExportFilePath, entries, groups, tags, key));
                    break;

                case ExportFormat.Csv:
                    await _keyManager.UseKeyAsync(key =>
                        _exportService.ExportCsvAsync(
                            ExportFilePath, entries, _cryptoService, key));
                    break;

                case ExportFormat.KeePassXml:
                    await _keyManager.UseKeyAsync(key =>
                        _exportService.ExportKeePassXmlAsync(
                            ExportFilePath, entries, groups, _cryptoService, key));
                    break;
            }

            ExportResult = L.F(
                "成功导出 {0} 条记录至 {1}",
                entries.Count,
                System.IO.Path.GetFileName(ExportFilePath));
            System.Diagnostics.Trace.TraceInformation("Export completed: {0}, {1} entries, path: {2}",
                SelectedFormat, entries.Count, ExportFilePath);

            ExportCompleted?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            ErrorMessage = L.F("导出失败：{0}", ex.Message);
            System.Diagnostics.Trace.TraceError("Export failed: format={0}, path={1}, error={2}", SelectedFormat, ExportFilePath, ex);
        }
        finally
        {
            IsExporting = false;
            ExportCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>
    /// Cancels the export dialog.
    /// </summary>
    [RelayCommand]
    private void Cancel()
    {
        Cancelled?.Invoke(this, EventArgs.Empty);
    }

    private bool CanExport()
    {
        return !IsExporting && !string.IsNullOrWhiteSpace(ExportFilePath);
    }

    private const string PlaintextExportWarning =
        "警告：此格式将以明文形式包含所有密码。请勿直接用表格软件打开；请安全保存并在导入后及时删除。需要可恢复的加密副本时，请使用“立即备份”。";

    #endregion

    partial void OnExportFilePathChanged(string? value)
    {
        ExportCommand.NotifyCanExecuteChanged();
    }
}

/// <summary>
/// Describes an export format option for display in the UI.
/// </summary>
public sealed class FormatDescription
{
    public ExportFormat Format { get; }
    public string DisplayName { get; }
    public string Description { get; }

    public FormatDescription(ExportFormat format, string displayName, string description)
    {
        Format = format;
        DisplayName = displayName;
        Description = description;
    }

    public override string ToString() => DisplayName;
}

/// <summary>
/// Event arguments for the browse file request, carrying the selected format
/// so the view can set the appropriate file filter.
/// </summary>
public sealed class BrowseFileEventArgs : EventArgs
{
    public ExportFormat Format { get; }

    public BrowseFileEventArgs(ExportFormat format)
    {
        Format = format;
    }
}
