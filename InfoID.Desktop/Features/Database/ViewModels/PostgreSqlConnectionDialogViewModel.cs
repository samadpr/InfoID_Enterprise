using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InfoID.Desktop.Core.Dialogs;
using InfoID.Desktop.Features.Database.Models;
using InfoID.Desktop.Features.Database.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace InfoID.Desktop.Features.Database.ViewModels;

public partial class PostgreSqlConnectionDialogViewModel
    : DialogViewModelBase<DatabaseConnection?>
{
    private readonly PostgreSqlDatabaseService _postgreSqlDatabaseService;

    public IReadOnlyList<string> SslConnectionOptions { get; } =
        new[]
        {
            "No",
            "Yes"
        };

    [ObservableProperty]
    private string _server = "localhost";

    [ObservableProperty]
    private string _port = "5432";

    [ObservableProperty]
    private string _username = "postgres";

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private string _database = string.Empty;

    [ObservableProperty]
    private string _sslConnection = "No";

    [ObservableProperty]
    private bool _keepCredentials;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public ObservableCollection<string> Databases { get; } = new();

    [ObservableProperty]
    private string? _selectedDatabase;

    public PostgreSqlConnectionDialogViewModel()
    {
        _postgreSqlDatabaseService =
            new PostgreSqlDatabaseService();
    }

    [RelayCommand]
    private void TestConnection()
    {
        if (string.IsNullOrWhiteSpace(Server))
        {
            StatusMessage = "Server is required.";
            return;
        }

        if (string.IsNullOrWhiteSpace(Username))
        {
            StatusMessage = "Username is required.";
            return;
        }

        try
        {
            var connectionString =
                _postgreSqlDatabaseService.BuildConnectionString(
                    Server,
                    Port,
                    "postgres",
                    Username,
                    Password,
                    SslConnection == "Yes");

            _postgreSqlDatabaseService.TestConnection(connectionString);

            var databases =
                _postgreSqlDatabaseService.GetDatabases(
                    Server,
                    Port,
                    Username,
                    Password,
                    SslConnection == "Yes");

            Databases.Clear();

            foreach (var database in databases)
            {
                Databases.Add(database);
            }

            StatusMessage =  $"Connection successful. Found {Databases.Count} databases.";
        }
        catch (Exception ex)
        {
            StatusMessage =  $"Connection failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Next()
    {
        if (string.IsNullOrWhiteSpace(SelectedDatabase))
        {
            StatusMessage =
                "Please select a database.";

            return;
        }

        try
        {
            var connectionString =
                _postgreSqlDatabaseService.BuildConnectionString(
                    Server,
                    Port,
                    SelectedDatabase,
                    Username,
                    Password,
                    SslConnection == "Yes");

            _postgreSqlDatabaseService
                .TestConnection(connectionString);

            var connection =
                new DatabaseConnection
                {
                    Name = SelectedDatabase,
                    Type = DatabaseType.PostgreSQL,
                    ConnectionString = connectionString,
                    Username = Username,
                    Password = Password,
                    KeepCredentials = KeepCredentials,
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
