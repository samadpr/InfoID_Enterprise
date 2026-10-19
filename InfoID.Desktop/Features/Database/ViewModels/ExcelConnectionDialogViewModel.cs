using System;
using System.Collections.ObjectModel;
using System.Data;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InfoID.Desktop.Core.Dialogs;
using InfoID.Desktop.Features.Database.Models;
using InfoID.Desktop.Features.Database.Services;

namespace InfoID.Desktop.Features.Database.ViewModels;

public partial class ExcelConnectionDialogViewModel
    : DialogViewModelBase<DatabaseConnection?>
{
    private readonly IFilePickerService _filePickerService;
    private readonly IDialogService _dialogService;
    private readonly ExcelOdbcService _excelOdbcService = new();

    [ObservableProperty]
    private string _filePath = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string? _selectedSheet;

    public ObservableCollection<string> Sheets { get; } = new();

    public ExcelConnectionDialogViewModel(
        IFilePickerService filePickerService, IDialogService dialogService)
    {
        _filePickerService = filePickerService;
        _dialogService = dialogService;
    }

    [RelayCommand]
    private async Task Browse()
    {
        var file = await _filePickerService.PickExcelFileAsync(
            "Select Excel File");

        if (string.IsNullOrWhiteSpace(file))
            return;

        FilePath = file;
        StatusMessage = string.Empty;
        Sheets.Clear();
        SelectedSheet = null;
    }

    [RelayCommand]
    private void TestConnection()
    {
        if (string.IsNullOrWhiteSpace(FilePath))
        {
            StatusMessage = "Please select an Excel file.";
            return;
        }

        try
        {
            var connectionString =
                $"Driver={{Microsoft Excel Driver (*.xls, *.xlsx, *.xlsm, *.xlsb)}};" +
                $"DBQ={FilePath};" +
                "ReadOnly=1;";

            if (!_excelOdbcService.TestConnection(
                    connectionString,
                    out var errorMessage))
            {
                StatusMessage = $"Connection failed: {errorMessage}";
                return;
            }

            var sheets = _excelOdbcService.GetSheets(connectionString);

            Sheets.Clear();

            foreach (DataRow row in sheets.Rows)
            {
                var tableName = row["TABLE_NAME"]?.ToString();

                if (string.IsNullOrWhiteSpace(tableName))
                    continue;

                Sheets.Add(tableName);
            }

            StatusMessage = $"Connected successfully. {Sheets.Count} sheet(s) found.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Connection failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task Preview()
    {
        if (string.IsNullOrWhiteSpace(FilePath))
        {
            StatusMessage = "Please select an Excel file.";
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedSheet))
        {
            StatusMessage = "Please select a worksheet.";
            return;
        }

        try
        {
            var connectionString =
                $"Driver={{Microsoft Excel Driver (*.xls, *.xlsx, *.xlsm, *.xlsb)}};" +
                $"DBQ={FilePath};" +
                "ReadOnly=1;";

            var data =
                _excelOdbcService.ReadSheet(
                    connectionString,
                    SelectedSheet);

            var previewDialog =
                new ExcelPreviewDialogViewModel(
                    data,
                    Path.GetFileName(FilePath),
                    SelectedSheet);

            await _dialogService.ShowDialogAsync<
                ExcelPreviewDialogViewModel,
                bool>(previewDialog);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Preview failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Next()
    {
        if (string.IsNullOrWhiteSpace(FilePath))
        {
            StatusMessage = "Please select an Excel file.";
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedSheet))
        {
            StatusMessage = "Please select a worksheet.";
            return;
        }

        var connectionString =
            $"Driver={{Microsoft Excel Driver (*.xls, *.xlsx, *.xlsm, *.xlsb)}};" +
            $"DBQ={FilePath};" +
            "ReadOnly=1;";

        var connection = new DatabaseConnection
        {
            Name = Path.GetFileName(FilePath),
            Type = DatabaseType.Excel,
            ConnectionString = connectionString,
            IsConnected = true
        };

        RequestClose(connection);
    }

    [RelayCommand]
    private void Cancel()
    {
        RequestClose(null);
    }
}
