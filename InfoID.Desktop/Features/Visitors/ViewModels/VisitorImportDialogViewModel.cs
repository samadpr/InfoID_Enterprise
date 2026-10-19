using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InfoID.Application.Dtos.VisitorModule;
using InfoID.Application.Interfaces;
using InfoID.Desktop.Core.Dialogs;
using InfoID.Desktop.Features.Visitors.Services;
using InfoID.Domain.Common.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace InfoID.Desktop.Features.Visitors.ViewModels;

/// <summary>Which file column feeds one visitor field (changeable in the review).</summary>
public sealed partial class VisitorFieldMappingViewModel : ObservableObject
{
    public const string NotInFile = "(not in the file)";

    public VisitorFieldMappingViewModel(VisitorImportField field, string label, string hint)
    {
        Field = field;
        Label = label;
        Hint = hint;
    }

    public VisitorImportField Field { get; }
    public string Label { get; }
    public string Hint { get; }
    public ObservableCollection<string> Columns { get; } = new();

    [ObservableProperty] private string _selectedColumn = NotInFile;

    public string? Column => SelectedColumn == NotInFile ? null : SelectedColumn;

    public event EventHandler? Changed;

    partial void OnSelectedColumnChanged(string value) => Changed?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// Bulk upload of visitors (Excel, CSV/TXT, JSON): choose the file → InfoID matches its
/// columns to the visitor fields → every row is checked (errors, warnings, already imported,
/// repeated in the file) and can be corrected in place → Import copies the photos into
/// InfoID and saves the rows that are ready. Rows with errors are never imported.
/// </summary>
public sealed partial class VisitorImportDialogViewModel : DialogViewModelBase<VisitorImportResult?>
{
    private static readonly InfoID.Shared.Logging.AppLogger Log = InfoID.Shared.Logging.AppLog.For<VisitorImportDialogViewModel>();

    private readonly IFilePickerService _filePicker;
    private readonly IServiceScopeFactory _scopes;
    private readonly VisitorPhotoStore _photos;
    private readonly VisitorFileReader _reader = new();
    private VisitorFileData? _file;
    private bool _applyingMapping;

    public VisitorImportDialogViewModel(IFilePickerService filePicker, IServiceScopeFactory scopes, VisitorPhotoStore? photos = null)
    {
        _filePicker = filePicker;
        _scopes = scopes;
        _photos = photos ?? new VisitorPhotoStore();

        Mappings = new ObservableCollection<VisitorFieldMappingViewModel>
        {
            new(VisitorImportField.FullName, "Full name", "Required"),
            new(VisitorImportField.Phone, "Phone", "Phone or email required"),
            new(VisitorImportField.Email, "Email", "Phone or email required"),
            new(VisitorImportField.Company, "Company", "Optional"),
            new(VisitorImportField.PersonToMeet, "Person to meet", "Optional"),
            new(VisitorImportField.Purpose, "Purpose", "Optional"),
            new(VisitorImportField.VisitDate, "Visit date", "Today if empty"),
            new(VisitorImportField.Photo, "Photo", "File name, path or (later) link"),
            new(VisitorImportField.ResponseKey, "Response ID / timestamp", "Stops repeat imports"),
        };
        foreach (var mapping in Mappings) mapping.Changed += (_, _) => { if (!_applyingMapping) _ = RebuildRowsAsync(); };
    }

    public override string Title => "Import visitors";
    public override bool CanResize => true;
    public override (double Width, double Height)? PreferredSize => (1200, 780);

    public ObservableCollection<VisitorFieldMappingViewModel> Mappings { get; }
    public ObservableCollection<VisitorImportRowViewModel> Rows { get; } = new();
    public ObservableCollection<string> Sheets { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFile))]
    private string _fileName = string.Empty;

    [ObservableProperty] private string? _selectedSheet;
    [ObservableProperty] private string? _photosFolder;
    [ObservableProperty] private bool _showOnlyProblems;
    [ObservableProperty] private VisitorImportRowViewModel? _selectedRow;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    private bool _isBusy;

    [ObservableProperty] private string? _message;
    [ObservableProperty] private bool _messageIsError;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    private int _importCount;

    [ObservableProperty] private int _readyCount;
    [ObservableProperty] private int _warningCount;
    [ObservableProperty] private int _errorCount;
    [ObservableProperty] private int _skippedCount;

    public bool HasFile => FileName.Length > 0;
    public bool HasSheets => Sheets.Count > 1;
    public bool HasPhotosFolder => !string.IsNullOrWhiteSpace(PhotosFolder);
    public string ImportButtonText => ImportCount == 1 ? "Import 1 visitor" : $"Import {ImportCount} visitors";

    /// <summary>The rows shown in the grid (all, or only those needing attention).</summary>
    public IEnumerable<VisitorImportRowViewModel> VisibleRows =>
        ShowOnlyProblems ? Rows.Where(r => r.Status != VisitorImportRowStatus.Ready) : Rows;

    partial void OnShowOnlyProblemsChanged(bool value) => OnPropertyChanged(nameof(VisibleRows));
    partial void OnImportCountChanged(int value) => OnPropertyChanged(nameof(ImportButtonText));
    partial void OnPhotosFolderChanged(string? value) { OnPropertyChanged(nameof(HasPhotosFolder)); if (_file is not null) _ = RebuildRowsAsync(); }

    partial void OnSelectedSheetChanged(string? value)
    {
        if (_file is null || value is null || value == _file.Sheet) return;
        _ = LoadFileAsync(_file.FilePath, value);
    }

    // ---------------------------------------------------------------- file ----

    [RelayCommand]
    private async Task ChooseFile()
    {
        var path = await _filePicker.PickOpenFileAsync("Choose the visitors file", "Visitor files (Excel, CSV, JSON)", VisitorFileReader.SupportedPatterns);
        if (string.IsNullOrWhiteSpace(path)) return;
        await LoadFileAsync(path, sheet: null);
    }

    /// <summary>Reads a file and matches its columns (also used by tests and drag-and-drop).</summary>
    public async Task LoadFileAsync(string path, string? sheet)
    {
        IsBusy = true;
        ShowMessage(null);
        try
        {
            var file = await _reader.ReadAsync(path, sheet);
            _file = file;
            FileName = file.FileName;

            Sheets.Clear();
            foreach (var s in file.Sheets) Sheets.Add(s);
#pragma warning disable MVVMTK0034 // set without re-reading the file
            _selectedSheet = file.Sheet;
#pragma warning restore MVVMTK0034
            OnPropertyChanged(nameof(SelectedSheet));
            OnPropertyChanged(nameof(HasSheets));

            ApplyMapping(VisitorColumnMatcher.Match(file.Columns), file.Columns);

            if (file.Table.Rows.Cast<System.Data.DataRow>().All(r => r.ItemArray.All(v => string.IsNullOrWhiteSpace(v?.ToString()))))
                ShowMessage("The file has no visitor rows.", isError: true);
        }
        catch (Exception ex)
        {
            Log.Warn("Visitor file couldn't be read", ex);
            _file = null;
            FileName = string.Empty;
            Rows.Clear();
            UpdateCounts();
            ShowMessage(ex.GetBaseException().Message, isError: true);
            IsBusy = false;
            return;
        }
        IsBusy = false;
        await RebuildRowsAsync();
    }

    private void ApplyMapping(IReadOnlyDictionary<VisitorImportField, string> matched, IReadOnlyList<string> columns)
    {
        _applyingMapping = true;
        try
        {
            foreach (var mapping in Mappings)
            {
                mapping.Columns.Clear();
                mapping.Columns.Add(VisitorFieldMappingViewModel.NotInFile);
                foreach (var column in columns) mapping.Columns.Add(column);
                mapping.SelectedColumn = matched.TryGetValue(mapping.Field, out var column1) ? column1 : VisitorFieldMappingViewModel.NotInFile;
            }
        }
        finally
        {
            _applyingMapping = false;
        }
    }

    [RelayCommand]
    private async Task ChoosePhotosFolder()
    {
        var folder = await _filePicker.PickFolderAsync("Folder with the visitors' photos");
        if (!string.IsNullOrWhiteSpace(folder)) PhotosFolder = folder;
    }

    [RelayCommand]
    private void ClearPhotosFolder() => PhotosFolder = null;

    /// <summary>Re-reads the rows with the current column matches and photos folder.</summary>
    private async Task RebuildRowsAsync()
    {
        if (_file is null) return;
        var mapping = Mappings.Where(m => m.Column is not null).ToDictionary(m => m.Field, m => m.Column!);
        var today = DateOnly.FromDateTime(DateTime.Today);

        IsBusy = true;
        try
        {
            var file = _file;
            var photosFolder = PhotosFolder;
            var rows = await Task.Run(() => VisitorImportBuilder.BuildRows(file, mapping, photosFolder, today));

            // Already imported (asked once for the whole file).
            IReadOnlySet<string> imported;
            using (var scope = _scopes.CreateScope())
            {
                imported = await scope.ServiceProvider.GetRequiredService<IVisitorService>()
                    .FindImportedSourceKeysAsync(rows.Select(r => r.SourceKey).ToList());
            }
            foreach (var row in rows.Where(r => imported.Contains(r.SourceKey)))
                row.MarkDuplicate(alreadyImported: true, duplicateInFile: row.IsDuplicateInFile);

            foreach (var row in Rows) row.Changed -= OnRowChanged;
            Rows.Clear();
            foreach (var row in rows)
            {
                row.Changed += OnRowChanged;
                Rows.Add(row);
            }
            UpdateCounts();
            OnPropertyChanged(nameof(VisibleRows));
            SelectedRow = Rows.FirstOrDefault(r => r.IsError) ?? Rows.FirstOrDefault();

            if (mapping.ContainsKey(VisitorImportField.FullName) == false)
                ShowMessage("Choose which column holds the visitor's name (Column matches, left).", isError: true);
            else if (!MessageIsError || Rows.Count > 0)
                ShowMessage(null);
        }
        catch (Exception ex)
        {
            Log.Warn("Visitor rows couldn't be checked", ex);
            ShowMessage("The rows couldn't be checked: " + ex.GetBaseException().Message, isError: true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OnRowChanged(object? sender, EventArgs e)
    {
        UpdateCounts();
        if (ShowOnlyProblems) OnPropertyChanged(nameof(VisibleRows));
    }

    private void UpdateCounts()
    {
        ReadyCount = Rows.Count(r => r.Status == VisitorImportRowStatus.Ready);
        WarningCount = Rows.Count(r => r.Status == VisitorImportRowStatus.Warning);
        ErrorCount = Rows.Count(r => r.Status == VisitorImportRowStatus.Error);
        SkippedCount = Rows.Count(r => r.IsSkipped);
        ImportCount = Rows.Count(r => r.CanImport);
    }

    // ------------------------------------------------------------ row photos ----

    [RelayCommand]
    private async Task ChooseRowPhoto(VisitorImportRowViewModel? row)
    {
        row ??= SelectedRow;
        if (row is null) return;
        var path = await _filePicker.PickImageFileAsync($"Photo for {(string.IsNullOrWhiteSpace(row.FullName) ? "this visitor" : row.FullName)}");
        if (string.IsNullOrWhiteSpace(path)) return;

        var problem = VisitorPhotoStore.CheckImage(path);
        row.PhotoText = Path.GetFileName(path);
        row.PhotoFile = problem is null ? path : null;
        row.PhotoProblem = problem;
    }

    [RelayCommand]
    private void RemoveRowPhoto(VisitorImportRowViewModel? row)
    {
        row ??= SelectedRow;
        if (row is null) return;
        row.PhotoText = string.Empty;
        row.PhotoFile = null;
        row.PhotoProblem = null;
    }

    // ---------------------------------------------------------------- import ----

    private bool CanImport() => !IsBusy && ImportCount > 0;

    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task Import()
    {
        var rows = Rows.Where(r => r.CanImport).ToList();
        if (rows.Count == 0) return;

        IsBusy = true;
        ShowMessage($"Importing {rows.Count} visitor{(rows.Count == 1 ? "" : "s")}…");
        var copiedPhotos = new List<string>();
        var photoFailures = 0;
        try
        {
            var request = new VisitorImportRequest { Source = VisitorSource.File, SourceName = FileName };
            foreach (var row in rows)
            {
                var item = row.ToInput(out _);
                item.PhotoPath = null;
                if (row.PhotoFile is not null)
                {
                    try
                    {
                        item.PhotoPath = await _photos.ImportAsync(row.PhotoFile);
                        copiedPhotos.Add(item.PhotoPath);
                    }
                    catch (Exception ex)
                    {
                        // The visitor is still imported; the photo can be added later.
                        photoFailures++;
                        Log.Warn($"Photo for import row {row.RowNumber} couldn't be copied", ex);
                    }
                }
                request.Items.Add(item);
            }

            VisitorImportResult result;
            using (var scope = _scopes.CreateScope())
                result = await scope.ServiceProvider.GetRequiredService<IVisitorService>().ImportAsync(request);

            // Photos of rows the service skipped (imported meanwhile) aren't used by anyone.
            var used = result.Created.Where(c => c.PhotoPath is not null).Select(c => c.PhotoPath!).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var photo in copiedPhotos.Where(p => !used.Contains(p))) _photos.Delete(photo);

            if (photoFailures > 0)
                Log.Info($"{photoFailures} visitor photo(s) couldn't be copied during import");
            RequestClose(result);
        }
        catch (Exception ex)
        {
            Log.Error("Visitor import failed", ex);
            foreach (var photo in copiedPhotos) _photos.Delete(photo);
            ShowMessage("The visitors couldn't be imported: " + ex.GetBaseException().Message, isError: true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Cancel() => RequestClose(null);

    private void ShowMessage(string? text, bool isError = false)
    {
        MessageIsError = isError && text is not null;
        Message = text;
    }
}
