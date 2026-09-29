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
    private readonly InternalSqliteDatabaseService _internalSqliteDatabaseService = new();
    private readonly PostgreSqlDatabaseService _postgreSqlDatabaseService = new();
    private readonly InternalSqliteDatabaseService _sampleSqliteDatabaseService = new("SampleDatabase.db");
    private readonly SqliteDatabaseService _sqliteDatabaseService = new();
    private readonly MySqlDatabaseService _mySqlDatabaseService = new();
    private readonly DatabaseType _connectionType;
    public DatabaseDesignDialogViewModel(DatabaseConnection connection,IDialogService dialogService)
    {
        _dialogService = dialogService;

        SelectedDatabase = connection.Name;

        ConnectionString = connection.ConnectionString;
        _connectionType = connection.Type;

        LoadTables();
    }
    private void LoadTables()
    {
        try
        {
            Tables.Clear();

            if (_connectionType == DatabaseType.InternalDatabase)
            {
                var tables =
                    _internalSqliteDatabaseService.GetTables();

                foreach (var table in tables)
                    Tables.Add(table);
            }
            else if (_connectionType == DatabaseType.SampleDatabase)
            {
                var tables =
                    _sampleSqliteDatabaseService.GetTables();

                foreach (var table in tables)
                    Tables.Add(table);
            }
            else if (_connectionType == DatabaseType.SQLite)
            {
                var tables =
                    _sqliteDatabaseService.GetTables(
                        ConnectionString);

                foreach (var table in tables)
                {
                    Tables.Add(table);
                }
            }
            else if (_connectionType == DatabaseType.MySQL)
            {
                var tables =
                    _mySqlDatabaseService.GetTables(
                        ConnectionString);

                foreach (var table in tables)
                {
                    Tables.Add(table);
                }
            }
            else if (_connectionType == DatabaseType.PostgreSQL)
            {
                var tables =
                    _postgreSqlDatabaseService.GetTables(ConnectionString);

                foreach (var table in tables)
                {
                    Tables.Add(table);
                }
            }
            else
            {
                var tables =
                    _odbcDatabaseService.GetTables(
                        ConnectionString);

                foreach (var table in tables)
                {
                    Tables.Add(table);
                }
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
    private bool _isNewTable;
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

        _isNewTable = false;

        LoadColumns(value);
    }
    private void LoadColumns(string tableName)
    {
        try
        {
            Columns.Clear();

            if (_connectionType == DatabaseType.InternalDatabase)
            {
                var columns =
                    _internalSqliteDatabaseService
                        .GetColumnDefinitions(tableName);

                foreach (var column in columns)
                {
                    Columns.Add(column);
                }
            }
            else if (_connectionType == DatabaseType.SampleDatabase)
            {
                var columns =
                    _sampleSqliteDatabaseService
                        .GetColumnDefinitions(tableName);

                foreach (var column in columns)
                {
                    Columns.Add(column);
                }
            }
            else if (_connectionType == DatabaseType.SQLite)
            {
                var columns =
                    _sqliteDatabaseService
                        .GetColumnDefinitions(
                            ConnectionString,
                            tableName);

                foreach (var column in columns)
                {
                    Columns.Add(column);
                }
            }
            else if (_connectionType == DatabaseType.MySQL)
            {
                var columns =
                    _mySqlDatabaseService
                        .GetColumnDefinitions(
                            ConnectionString,
                            tableName);

                foreach (var column in columns)
                {
                    Columns.Add(column);
                }
            }
            else if (_connectionType == DatabaseType.PostgreSQL)
            {
                var columns =
                    _postgreSqlDatabaseService.GetColumnDefinitions(
                        ConnectionString,
                        tableName);

                foreach (var column in columns)
                {
                    Columns.Add(column);
                }
            }
            else
            {
                var columns =
                    _odbcDatabaseService
                        .GetColumnDefinitions(
                            ConnectionString,
                            tableName);

                foreach (var column in columns)
                {
                    Columns.Add(column);
                }
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

    //[RelayCommand]
    //private void Finish()
    //{
    //    var primaryKey =
    //        Columns.FirstOrDefault(column =>
    //            column.IsPrimaryKey);

    //    if (primaryKey is null)
    //    {
    //        StatusMessage =
    //            "Primary key must be defined.";

    //        return;
    //    }

    //    var definition = new DatabaseTableDefinition
    //    {
    //        DatabaseName = SelectedDatabase,
    //        TableName = SelectedTable,
    //        ConnectionString = ConnectionString,
    //        PrimaryKey = primaryKey.Name,
    //        Columns = Columns
    //     .Where(column => column.IsSelected)
    //     .ToList()
    //    };

    //    RequestClose(definition);
    //}

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

        if (string.IsNullOrWhiteSpace(SelectedTable))
        {
            StatusMessage =
                "Please select or create a table.";

            return;
        }

        var selectedColumns =
            Columns
                .Where(column => column.IsSelected)
                .ToList();

        if (selectedColumns.Count == 0)
        {
            StatusMessage =
                "At least one column must be selected.";

            return;
        }

        var definition = new DatabaseTableDefinition
        {
            DatabaseName = SelectedDatabase,
            TableName = SelectedTable,
            ConnectionString = ConnectionString,
            PrimaryKey = primaryKey.Name,
            Columns = selectedColumns
        };

        try
        {
            if (_connectionType == DatabaseType.InternalDatabase)
            {
                if (_isNewTable)
                {
                    _internalSqliteDatabaseService
                        .CreateTable(definition);
                }
                else
                {
                    _internalSqliteDatabaseService
                        .AddMissingColumns(definition);
                }
            }

            RequestClose(definition);
        }
        catch (Exception ex)
        {
            StatusMessage =
                $"Could not create table: {ex.Message}";
        }
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
    private void AddTable()
    {
        var baseName = "Table";
        var tableNumber = 1;

        var tableName = $"{baseName}{tableNumber}";

        while (Tables.Contains(tableName))
        {
            tableNumber++;
            tableName = $"{baseName}{tableNumber}";
        }

        Tables.Add(tableName);

        SelectedTable = tableName;

        Columns.Clear();

        Columns.Add(new DatabaseColumn
        {
            Name = "ID",
            DataType = "Integer",
            IsPrimaryKey = true,
            IsSelected = true
        });
        _isNewTable = true;
        StatusMessage =
            $"Table '{tableName}' created in the designer.";
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
