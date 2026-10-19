using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InfoID.Desktop.Core.Dialogs;
using InfoID.Desktop.Core.Services;
using InfoID.Desktop.Features.Database.Models;
using InfoID.Desktop.Features.Database.Services;
using System;
using System.Threading.Tasks;

namespace InfoID.Desktop.Features.Database.ViewModels;

public partial class SqliteConnectionDialogViewModel
    : DialogViewModelBase<DatabaseConnection?>
{
    private readonly IFilePickerService _filePickerService;
    private readonly SqliteDatabaseService _sqliteDatabaseService;

    [ObservableProperty]
    private string _filePath = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public SqliteConnectionDialogViewModel(
        IFilePickerService filePickerService)
    {
        _filePickerService = filePickerService;
        _sqliteDatabaseService = new SqliteDatabaseService();
    }

    [RelayCommand]
    private async Task Browse()
    {
        var path =  await _filePickerService.PickOpenFileAsync(
        "Select SQLite Database",
        "SQLite Database Files",
        new[]
        {
            "*.db",
            "*.sqlite",
            "*.sqlite3"
        });

        if (string.IsNullOrWhiteSpace(path))
            return;

        FilePath = path;
        StatusMessage = string.Empty;
    }

    [RelayCommand]
    private void TestConnection()
    {
        if (string.IsNullOrWhiteSpace(FilePath))
        {
            StatusMessage =
                "Please select a SQLite database file.";

            return;
        }

        try
        {
            var connectionString =
                _sqliteDatabaseService
                    .BuildConnectionString(FilePath);

            _sqliteDatabaseService
                .TestConnection(connectionString);

            StatusMessage =
                "SQLite connection successful.";
        }
        catch (Exception ex)
        {
            StatusMessage =
                $"Connection failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Next()
    {
        if (string.IsNullOrWhiteSpace(FilePath))
        {
            StatusMessage =
                "Please select a SQLite database file.";

            return;
        }

        try
        {
            var connectionString =
                _sqliteDatabaseService
                    .BuildConnectionString(FilePath);

            _sqliteDatabaseService
                .TestConnection(connectionString);

            var connection =
                new DatabaseConnection
                {
                    Name =
                        System.IO.Path.GetFileName(
                            FilePath),

                    Type =
                        DatabaseType.SQLite,

                    ConnectionString =
                        connectionString,

                    IsConnected = true
                };

            RequestClose(connection);
        }
        catch (Exception ex)
        {
            StatusMessage =
                $"Connection failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        RequestClose(null);
    }
}
