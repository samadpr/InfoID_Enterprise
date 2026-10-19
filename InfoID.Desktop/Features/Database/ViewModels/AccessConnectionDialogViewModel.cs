using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InfoID.Desktop.Core.Dialogs;
using InfoID.Desktop.Features.Database.Models;
using InfoID.Desktop.Features.Database.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace InfoID.Desktop.Features.Database.ViewModels;

public sealed partial class AccessConnectionDialogViewModel
    : DialogViewModelBase<DatabaseConnection?>
{
    private readonly IFilePickerService _filePickerService;

    public override string Title =>
        "Connect to Microsoft Access";

    [ObservableProperty]
    private string _filePath = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public ObservableCollection<string> Tables { get; } = new();

    [ObservableProperty]
    private string? _selectedTable;

    partial void OnSelectedTableChanged(string? value)
    {
        Fields.Clear();

        if (string.IsNullOrWhiteSpace(value))
            return;

        try
        {
            var service = new OdbcDatabaseService();

            var fields =
                service.GetColumnDefinitions(
                    BuildAccessConnectionString(),
                    value);

            foreach (var field in fields)
            {
                Fields.Add(field);
            }
        }
        catch (Exception ex)
        {
            StatusMessage =
                $"Could not load fields: {ex.Message}";
        }
    }
    public ObservableCollection<DatabaseColumn> Fields { get; } = new(); 

    public AccessConnectionDialogViewModel(
        IFilePickerService filePickerService)
    {
        _filePickerService = filePickerService;
    }

    [RelayCommand]
    private async Task Browse()
    {
        try
        {
            var filePath =
                await _filePickerService.PickOpenFileAsync(
                    "Select Access Database",
                    "Access Database",
                    new[]
                    {
                        "*.accdb",
                        "*.mdb"
                    });

            if (string.IsNullOrWhiteSpace(filePath))
                return;

            FilePath = filePath;

            StatusMessage =
                $"Selected: " +
                $"{System.IO.Path.GetFileName(FilePath)}";
        }
        catch (Exception ex)
        {
            StatusMessage =
                $"Could not select file: {ex.Message}";
        }
    }

    private string BuildAccessConnectionString()
    {
        var service =
            new AccessDatabaseService();

        return service.BuildConnectionString(FilePath);
    }

    [RelayCommand]
    private void TestConnection()
    {
        if (string.IsNullOrWhiteSpace(FilePath))
        {
            StatusMessage =
                "Please select an Access database.";

            return;
        }

        try
        {
            StatusMessage =
                "Testing Access database connection...";

            var service =
                new AccessDatabaseService();

            var connectionString =
    service.BuildConnectionString(FilePath);

            service.TestConnection(connectionString);

            Tables.Clear();

            var tables =
                service.GetTables(connectionString);

            foreach (var table in tables)
            {
                Tables.Add(table);
            }

            StatusMessage =
                $"{Tables.Count} table(s) found.";
        }
        catch (Exception ex)
        {
            StatusMessage =
                $"Connection failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Previous()
    {
        RequestClose(null);
    }

    [RelayCommand]
    private void Next()
    {
        if (string.IsNullOrWhiteSpace(FilePath))
        {
            StatusMessage =
                "Please select an Access database.";

            return;
        }
        if (string.IsNullOrWhiteSpace(SelectedTable))
        {
            StatusMessage =
                "Please select a table.";

            return;
        }

        try
        {
            var service =
                new AccessDatabaseService();

            var connectionString =
                service.BuildConnectionString(
                    FilePath);

            var connection =
      new DatabaseConnection
      {
          Name =
              System.IO.Path.GetFileName(
                  FilePath),

          Type =
              DatabaseType.Access,

          ConnectionString =
              connectionString,

          SelectedTable =
              SelectedTable,
          SelectedColumns =
    Fields
        .Where(x => x.IsSelected)
        .ToList(),

          IsConnected = true
      };

            RequestClose(connection);
        }
        catch (Exception ex)
        {
            StatusMessage =
                $"Could not connect: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        RequestClose(null);
    }
}
