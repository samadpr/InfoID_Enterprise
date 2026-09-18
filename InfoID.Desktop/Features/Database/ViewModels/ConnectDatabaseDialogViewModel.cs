using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InfoID.Desktop.Core.Dialogs;
using InfoID.Desktop.Core.Services;
using InfoID.Desktop.Features.Database.Models;
using InfoID.Desktop.Features.Database.Services;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Data;
namespace InfoID.Desktop.Features.Database.ViewModels;

public sealed partial class ConnectDatabaseDialogViewModel
    : DialogViewModelBase<bool>
{
    public ConnectDatabaseDialogViewModel(IFilePickerService filePicker)
    {
        _filePicker = filePicker;
    }
    private readonly ExcelOdbcService _excelOdbcService = new();
    private readonly IFilePickerService _filePicker;
    public override string Title => "Connect to database";

    public DatabaseType[] DatabaseTypes { get; } =
{
    DatabaseType.SQLite,
    DatabaseType.MySQL,
    DatabaseType.PostgreSQL,
    DatabaseType.SQLServer,
    DatabaseType.ODBC
};

    [RelayCommand]
    private async Task BrowseExcelFile()
    {
        var filePath = await _filePicker.PickExcelFileAsync("Select Excel File");

        if (string.IsNullOrWhiteSpace(filePath))
            return;

        FilePath = filePath;

        ConnectionString =
            $"Driver={{Microsoft Excel Driver (*.xls, *.xlsx, *.xlsm, *.xlsb)}};" +
            $"DBQ={FilePath};" +
            "ReadOnly=1;";

        StatusMessage = "Excel file selected.";
    }
    [ObservableProperty]
    private DatabaseType _selectedDatabaseType = DatabaseType.ODBC;

    [ObservableProperty]
    private string _connectionName = "My Database";

    [ObservableProperty]
    private string _connectionString = string.Empty;

    [ObservableProperty]
    private string _filePath = string.Empty;

    [ObservableProperty]
    private string _selectedSheet = string.Empty;
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public ObservableCollection<string> Sheets { get; } = new();

    [ObservableProperty]
    private DataTable? _loadedData;

    public ObservableCollection<ExcelPreviewRow> PreviewRows { get; } = new();

    [RelayCommand]
    private void Cancel()
    {
        RequestClose(false);
    }
    [RelayCommand]
    private void TestConnection()
    {
        if (string.IsNullOrWhiteSpace(FilePath))
        {
            StatusMessage = "Please select an Excel file first.";
            return;
        }

        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            StatusMessage = "Connection string is empty.";
            return;
        }

        StatusMessage = "Testing connection...";

        var success = _excelOdbcService.TestConnection(
            ConnectionString,
            out var errorMessage);

        if (success)
        {
            StatusMessage = "Connection successful.";

            LoadSheets();
        }
        else
        {
            StatusMessage = $"Connection failed: {errorMessage}";
        }
    }
    [RelayCommand]
    private void ReadSelectedSheet()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            StatusMessage = "Please test the connection first.";
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedSheet))
        {
            StatusMessage = "Please select an Excel sheet.";
            return;
        }

        try
        {
            var table = _excelOdbcService.ReadSheet( ConnectionString, SelectedSheet);

            LoadedData = table;

            PreviewRows.Clear();

            foreach (DataRow row in table.Rows)
            {
                var previewRow = new ExcelPreviewRow();

                foreach (DataColumn column in table.Columns)
                {
                    previewRow.Values[column.ColumnName] = row[column];
                }

                PreviewRows.Add(previewRow);
            }

            StatusMessage =
                $"Successfully loaded {table.Rows.Count} row(s) and {table.Columns.Count} column(s).";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not read the sheet: {ex.Message}";
        }
    }
    private void LoadSheets()
    {
        try
        {
            Sheets.Clear();

            var tables = _excelOdbcService.GetSheets(ConnectionString);

            foreach (System.Data.DataRow row in tables.Rows)
            {
                var tableName = row["TABLE_NAME"]?.ToString();

                if (string.IsNullOrWhiteSpace(tableName))
                    continue;

                Sheets.Add(tableName);
            }

            if (Sheets.Count > 0)
            {
                SelectedSheet = Sheets[0];
                StatusMessage = $"{Sheets.Count} Excel sheet(s) found.";
            }
            else
            {
                StatusMessage = "No Excel sheets were found.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not read Excel sheets: {ex.Message}";
        }
    }
    [RelayCommand]
    private void Connect()
    {
        if (string.IsNullOrWhiteSpace(ConnectionName))
        {
            StatusMessage = "Please enter a connection name.";
            return;
        }

        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            StatusMessage = "Please enter a connection string.";
            return;
        }

        // We will implement the real database connection next.
        StatusMessage = "Connection details are valid.";

        RequestClose(true);
    }
}
