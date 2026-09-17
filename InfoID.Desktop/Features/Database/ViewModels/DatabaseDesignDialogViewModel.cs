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

public sealed partial class DatabaseDesignDialogViewModel
    : DialogViewModelBase<DatabaseTableDefinition?>
{
    private readonly OdbcDatabaseService _odbcDatabaseService = new();
    private readonly IDialogService _dialogService;
    public DatabaseDesignDialogViewModel(DatabaseConnection connection,IDialogService dialogService)
    {
        _dialogService = dialogService;

        SelectedDatabase = connection.Name;

        ConnectionString = connection.ConnectionString;

        LoadTables();
    }
    private void LoadTables()
    {
        try
        {
            Tables.Clear();

            var tables = _odbcDatabaseService.GetTables( ConnectionString);

            foreach (var table in tables)
            {
                Tables.Add(table);
            }

            if (Tables.Count > 0)
            {
                SelectedTable = Tables[0];

                StatusMessage =
                    $"{Tables.Count} table(s) found.";
            }
            else
            {
                StatusMessage =
                    "No tables were found.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage =
                $"Could not load tables: {ex.Message}";
        }
    }
    public override string Title => "Connect to database...";

    public ObservableCollection<string> Tables { get; } = new();

    public ObservableCollection<DatabaseColumn> Columns { get; } = new();

    [ObservableProperty]
    private string _selectedTable = string.Empty;

    partial void OnSelectedTableChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Columns.Clear();
            return;
        }

        LoadColumns(value);
    }
    private void LoadColumns(string tableName)
    {
        try
        {
            Columns.Clear();

            var columns =
                _odbcDatabaseService.GetColumnDefinitions(
                    ConnectionString,
                    tableName);

            foreach (var column in columns)
            {
                Columns.Add(column);
            }

            StatusMessage =
                Columns.Count > 0
                    ? $"{Columns.Count} column(s) found."
                    : "No columns were found.";
        }
        catch (Exception ex)
        {
            StatusMessage =
                $"Could not load columns: {ex.Message}";
        }
    }
    [ObservableProperty]
    private string _selectedDatabase = string.Empty;

    [ObservableProperty]
    private string _connectionString = string.Empty;
    [ObservableProperty]
    private string _statusMessage = string.Empty;

   

    [RelayCommand]
    private void Previous()
    {
        RequestClose(null);
    }

    [RelayCommand]
    private void Finish()
    {
        var primaryKey =
            Columns.FirstOrDefault(column =>
                column.IsPrimaryKey);

        if (primaryKey is null)
        {
            StatusMessage =
                "Primary key must be defined.";

            return;
        }

        var definition = new DatabaseTableDefinition
        {
            DatabaseName = SelectedDatabase,
            TableName = SelectedTable,
            ConnectionString = ConnectionString,
            PrimaryKey = primaryKey.Name,
            Columns = Columns
         .Where(column => column.IsSelected)
         .ToList()
        };

        RequestClose(definition);
    }
    [RelayCommand]
    private void Cancel()
    {
        RequestClose(null);
    }

    [RelayCommand]
    private async Task AddColumn()
    {
        var dialog = new AddColumnDialogViewModel();

        var column = await _dialogService.ShowDialogAsync< AddColumnDialogViewModel, DatabaseColumn?>(dialog);

        if (column is null)
            return;

        Columns.Add(column);

        StatusMessage = $"Column '{column.Name}' added.";
    }
    [RelayCommand]
    private void DeleteColumn(DatabaseColumn? column)
    {
        if (column is null)
            return;

        if (column.IsPrimaryKey)
        {
            StatusMessage =
                "A primary key column cannot be deleted. Remove the primary key first.";

            return;
        }

        if (Columns.Remove(column))
        {
            StatusMessage =
                $"Column '{column.Name}' removed.";
        }
    }
    [RelayCommand]
    private void Filter()
    {
        StatusMessage = "Filter functionality will be added next.";
    }
}
