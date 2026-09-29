using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InfoID.Desktop.Core.Dialogs;
using InfoID.Desktop.Features.Database.Models;
using System;
using System.Data;
using System.Data.Odbc;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory.Database;

namespace InfoID.Desktop.Features.Database.ViewModels;

public sealed partial class SqlServerConnectionDialogViewModel
    : DialogViewModelBase<DatabaseConnection?>
{
    public override string Title => "Connect to SQL Server";

    [ObservableProperty]
    private string _server = string.Empty;

    [ObservableProperty]
    private string _databaseName = string.Empty;

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private bool _windowsAuthentication = true;

    [ObservableProperty]
    private bool _keepCredentials;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    private string BuildConnectionString()
    {
        if (string.IsNullOrWhiteSpace(Server) ||
            string.IsNullOrWhiteSpace(DatabaseName))
        {
            return string.Empty;
        }

        if (WindowsAuthentication)
        {
            return
                "Driver={ODBC Driver 18 for SQL Server};" +
                $"Server={Server};" +
                $"Database={DatabaseName};" +
                "Trusted_Connection=Yes;" +
                "TrustServerCertificate=Yes;";
        }

        if (string.IsNullOrWhiteSpace(Username) ||
            string.IsNullOrWhiteSpace(Password))
        {
            return string.Empty;
        }

        return
            "Driver={ODBC Driver 18 for SQL Server};" +
            $"Server={Server};" +
            $"Database={DatabaseName};" +
            $"UID={Username};" +
            $"PWD={Password};" +
            "TrustServerCertificate=Yes;";
    }

    [RelayCommand]
    private void TestConnection()
    {
        var connectionString = BuildConnectionString();

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            StatusMessage =
                "Please enter the required connection details.";

            return;
        }

        try
        {
            StatusMessage =
                "Testing SQL Server connection...";

            using var connection =
                new OdbcConnection(connectionString);

            connection.Open();

            if (connection.State == ConnectionState.Open)
            {
                StatusMessage =
                    "SQL Server connection successful.";
            }
            else
            {
                StatusMessage =
                    "Connection could not be opened.";
            }
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
        var connectionString = BuildConnectionString();

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            StatusMessage =
                "Please enter the required connection details.";

            return;
        }

        var connection = new DatabaseConnection
        {
            Name = DatabaseName,
            Type = DatabaseType.SQLServer,
            ConnectionString = connectionString,
            Username = Username,
            Password = Password,
            KeepCredentials = KeepCredentials,
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
