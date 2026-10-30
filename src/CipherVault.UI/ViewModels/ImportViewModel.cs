using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CipherVault.Core.Interfaces;
using CipherVault.Core.Models;
using CipherVault.UI.Localization;

namespace CipherVault.UI.ViewModels;

/// <summary>
/// ViewModel for the import wizard window.
/// Manages the multi-step CSV import process: file selection, format detection,
/// field mapping, data preview, and final import execution.
/// </summary>
public partial class ImportViewModel : ViewModelBase
{
    private readonly IImportService _importService;
    private readonly IVaultRepository _repository;
    private string? _csvContent;

    /// <summary>
    /// Occurs when the import operation completes successfully.
    /// </summary>
    public event EventHandler<ImportResult>? ImportCompleted;

    /// <summary>
    /// Occurs when the user cancels the import wizard.
    /// </summary>
    public event EventHandler? Cancelled;

    public ImportViewModel(IImportService importService, IVaultRepository repository)
    {
        _importService = importService ?? throw new ArgumentNullException(nameof(importService));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));

        FieldMappings = new ObservableCollection<FieldMapping>();
        PreviewRows = new ObservableCollection<ImportPreviewRow>();
        Groups = new ObservableCollection<VaultGroup>();

        _ = LoadGroupsAsync();
    }

    private async Task LoadGroupsAsync()
    {
        try
        {
            var groups = await _repository.GetGroupsAsync();
            Groups.Clear();
            // Add a "root" option
            Groups.Add(new VaultGroup { Id = string.Empty, Name = L.T("(根目录)") });
            foreach (var group in groups)
            {
                Groups.Add(group);
            }
            SelectedTargetGroup = Groups.FirstOrDefault();
        }
        catch (Exception ex)
        {
            OnError(ex, "loading groups");
        }
    }

    #region Properties

    /// <summary>
    /// Current step in the wizard (1-4).
    /// </summary>
    [ObservableProperty]
    private int _currentStep = 1;

    /// <summary>
    /// Path to the selected CSV file.
    /// </summary>
    [ObservableProperty]
    private string? _filePath;

    /// <summary>
    /// Detected CSV format (e.g., "Chrome", "Firefox", "KeePass", "Unknown").
    /// </summary>
    [ObservableProperty]
    private string? _detectedFormat;

    /// <summary>
    /// Column-to-property field mappings.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<FieldMapping> _fieldMappings;

    /// <summary>
    /// First 10 rows of the CSV for preview.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<ImportPreviewRow> _previewRows;

    /// <summary>
    /// Total number of data rows in the CSV (excluding header).
    /// </summary>
    [ObservableProperty]
    private int _totalRowCount;

    /// <summary>
    /// Selected target group for imported entries.
    /// </summary>
    [ObservableProperty]
    private VaultGroup? _selectedTargetGroup;

    /// <summary>
    /// Available groups for the target selection.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<VaultGroup> _groups;

    /// <summary>
    /// Result of the import operation.
    /// </summary>
    [ObservableProperty]
    private ImportResult? _importResult;

    /// <summary>
    /// Whether an import operation is currently in progress.
    /// </summary>
    [ObservableProperty]
    private bool _isImporting;

    /// <summary>
    /// Error message to display to the user.
    /// </summary>
    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>
    /// Display name of the selected file (derived from FilePath).
    /// </summary>
    [ObservableProperty]
    private string? _selectedFileName;

    /// <summary>
    /// Whether the Next button should be enabled.
    /// </summary>
    public bool CanGoNext => CurrentStep switch
    {
        1 => !string.IsNullOrEmpty(FilePath) && File.Exists(FilePath),
        2 => FieldMappings.Count > 0,
        3 => PreviewRows.Count > 0,
        4 => !IsImporting,
        _ => false
    };

    /// <summary>
    /// Whether the Back button should be enabled.
    /// </summary>
    public bool CanGoBack => CurrentStep > 1 && !IsImporting;

    /// <summary>
    /// Step indicator text (e.g., "步骤 1/4: 选择文件").
    /// </summary>
    public string StepIndicatorText => CurrentStep switch
    {
        1 => L.T("步骤 1/4: 选择文件"),
        2 => L.T("步骤 2/4: 字段映射"),
        3 => L.T("步骤 3/4: 数据预览"),
        4 => L.T("步骤 4/4: 确认导入"),
        _ => string.Empty
    };

    /// <summary>
    /// Whether the import has completed and the result panel should be shown.
    /// </summary>
    [ObservableProperty]
    private bool _isImportFinished;

    /// <summary>
    /// Summary text for the import result.
    /// </summary>
    public string ResultSummaryText => ImportResult != null
        ? L.F(
            "成功 {0} 条，跳过 {1} 条，失败 {2} 条",
            ImportResult.SuccessCount,
            ImportResult.SkippedCount,
            ImportResult.FailedCount)
        : string.Empty;

    #endregion

    #region Commands

    /// <summary>
    /// Opens a file dialog for selecting a CSV file.
    /// </summary>
    [RelayCommand]
    private void BrowseFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = L.T("选择 CSV 文件"),
            Filter = L.T("CSV 文件 (*.csv)|*.csv|所有文件 (*.*)|*.*"),
            FilterIndex = 0
        };

        if (dialog.ShowDialog() == true)
        {
            FilePath = dialog.FileName;
            SelectedFileName = Path.GetFileName(dialog.FileName);
            ErrorMessage = null;

            // Auto-detect format after file selection
            DetectFormatInternal();
        }
    }

    /// <summary>
    /// Handles drag-and-drop file selection.
    /// </summary>
    [RelayCommand]
    private void SetFilePath(string path)
    {
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            FilePath = path;
            SelectedFileName = Path.GetFileName(path);
            ErrorMessage = null;
            DetectFormatInternal();
        }
    }

    /// <summary>
    /// Auto-detects the CSV format and builds field mappings.
    /// </summary>
    [RelayCommand]
    private void DetectFormat()
    {
        DetectFormatInternal();
    }

    private void DetectFormatInternal()
    {
        if (string.IsNullOrEmpty(FilePath) || !File.Exists(FilePath))
            return;

        try
        {
            var fileInfo = new FileInfo(FilePath);
            if (fileInfo.Length > 50L * 1024 * 1024)
                throw new InvalidDataException(L.T("CSV 文件超过 50 MB 限制，请拆分后导入。"));

            _csvContent = File.ReadAllText(FilePath);

            DetectedFormat = _importService.DetectFormat(_csvContent);

            // Build field mappings based on detected format
            BuildFieldMappings();

            // Load preview rows
            LoadPreview();

            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            ErrorMessage = L.F("无法读取文件: {0}", ex.Message);
            OnError(ex, "detecting CSV format");
        }

        OnPropertyChanged(nameof(CanGoNext));
    }

    private void BuildFieldMappings()
    {
        FieldMappings.Clear();

        if (string.IsNullOrEmpty(_csvContent))
            return;

        var preview = _importService.PreviewImport(_csvContent, DetectedFormat ?? "Unknown", 1);
        if (preview.Count == 0)
            return;

        var firstRow = preview[0];
        var availableFields = new[] { "Title", "Username", "Password", "Url", "Notes", L.T(IgnoreField) };

        foreach (var column in firstRow.Fields.Keys)
        {
            var mapping = new FieldMapping
            {
                CsvColumn = column,
                SampleValue = firstRow.Fields[column],
                AvailableFields = availableFields
            };

            // Auto-map based on common column names
            mapping.MappedField = AutoMapField(column);

            FieldMappings.Add(mapping);
        }
    }

    private static string AutoMapField(string csvColumn)
    {
        var lower = csvColumn.ToLowerInvariant();

        if (lower is "name" or "title" or "entry" or "网站名称")
            return "Title";
        if (lower is "username" or "user" or "login" or "email" or "账号" or "用户名" or "登录名")
            return "Username";
        if (lower is "password" or "pass" or "密码")
            return "Password";
        if (lower is "url" or "website" or "link" or "网址" or "链接")
            return "Url";
        if (lower is "notes" or "comment" or "备注" or "注释" or "note")
            return "Notes";

        return L.T(IgnoreField);
    }

    private void LoadPreview()
    {
        PreviewRows.Clear();

        if (string.IsNullOrEmpty(_csvContent))
            return;

        try
        {
            var rows = _importService.PreviewImport(_csvContent, DetectedFormat ?? "Unknown", 10);
            foreach (var row in rows)
            {
                PreviewRows.Add(row);
            }

            TotalRowCount = _importService.CountDataRows(_csvContent);
        }
        catch (Exception ex)
        {
            OnError(ex, "loading preview");
        }
    }

    /// <summary>
    /// Advances to the next step in the wizard.
    /// </summary>
    [RelayCommand]
    private void NextStep()
    {
        if (CurrentStep < 4)
        {
            CurrentStep++;
            OnPropertyChanged(nameof(StepIndicatorText));
            OnPropertyChanged(nameof(CanGoNext));
            OnPropertyChanged(nameof(CanGoBack));
        }
    }

    /// <summary>
    /// Goes back to the previous step in the wizard.
    /// </summary>
    [RelayCommand]
    private void PreviousStep()
    {
        if (CurrentStep > 1)
        {
            CurrentStep--;
            OnPropertyChanged(nameof(StepIndicatorText));
            OnPropertyChanged(nameof(CanGoNext));
            OnPropertyChanged(nameof(CanGoBack));
        }
    }

    /// <summary>
    /// Executes the import operation with the configured mappings and target group.
    /// </summary>
    [RelayCommand]
    private async Task StartImportAsync()
    {
        if (string.IsNullOrEmpty(_csvContent))
        {
            ErrorMessage = L.T("未选择文件");
            return;
        }

        IsImporting = true;
        ErrorMessage = null;

        try
        {
            // Build field mapping dictionary
            var fieldMapping = new Dictionary<string, string>();
            foreach (var mapping in FieldMappings)
            {
                if (!string.IsNullOrEmpty(mapping.MappedField)
                    && mapping.MappedField != IgnoreField
                    && mapping.MappedField != L.T(IgnoreField))
                {
                    fieldMapping[mapping.CsvColumn] = mapping.MappedField;
                }
            }

            var targetGroupId = SelectedTargetGroup?.Id;
            if (string.IsNullOrEmpty(targetGroupId))
                targetGroupId = null;

            ImportResult = await _importService.ImportAsync(
                _csvContent,
                DetectedFormat ?? "Unknown",
                fieldMapping,
                targetGroupId);

            IsImportFinished = true;
            OnPropertyChanged(nameof(ResultSummaryText));

            ImportCompleted?.Invoke(this, ImportResult);
            _csvContent = null;
        }
        catch (Exception ex)
        {
            ErrorMessage = L.F("导入失败: {0}", ex.Message);
            OnError(ex, "importing CSV");
        }
        finally
        {
            IsImporting = false;
        }
    }

    /// <summary>
    /// Cancels the import wizard.
    /// </summary>
    [RelayCommand]
    private void Cancel()
    {
        Cancelled?.Invoke(this, EventArgs.Empty);
    }

    private const string IgnoreField = "(忽略)";

    /// <summary>
    /// Closes the wizard after import is finished.
    /// </summary>
    [RelayCommand]
    private void Finish()
    {
        ImportCompleted?.Invoke(this, ImportResult!);
    }

    #endregion

    protected override void OnError(Exception ex, string errorContext)
    {
        base.OnError(ex, errorContext);
        ErrorMessage = ex.Message;
    }
}

#region Helper Classes

/// <summary>
/// Represents a mapping from a CSV column to a VaultEntry property.
/// </summary>
public sealed class FieldMapping : ObservableObject
{
    /// <summary>
    /// The CSV column header name.
    /// </summary>
    public string CsvColumn { get; init; } = string.Empty;

    /// <summary>
    /// The mapped VaultEntry property name (Title, Username, Password, Url, Notes, or "(忽略)").
    /// </summary>
    public string MappedField
    {
        get => _mappedField;
        set => SetProperty(ref _mappedField, value);
    }
    private string _mappedField = "(忽略)";

    /// <summary>
    /// A sample value from the first data row for this column.
    /// </summary>
    public string SampleValue { get; init; } = string.Empty;

    /// <summary>
    /// Available target fields for mapping.
    /// </summary>
    public string[] AvailableFields { get; init; } = Array.Empty<string>();
}

#endregion
