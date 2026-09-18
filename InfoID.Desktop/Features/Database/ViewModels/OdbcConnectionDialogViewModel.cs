using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InfoID.Desktop.Core.Dialogs;
using System;
using System.Collections.ObjectModel;
using System.Data.Common;
using System.Data.Odbc;
using System.Diagnostics;
using Microsoft.Win32;
using InfoID.Desktop.Features.Database.Models;

namespace InfoID.Desktop.Features.Database.ViewModels;

public sealed partial class OdbcConnectionDialogViewModel
    : DialogViewModelBase<DatabaseConnection?>
{
    public OdbcConnectionDialogViewModel()
    {
        LoadOdbcDataSources();
    }

    public override string Title => "Connect to database...";
    public DatabaseConnection? Connection { get; private set; }
    public ObservableCollection<string> DatabaseOptions { get; } = new();
    

    [ObservableProperty]
    private string _selectedDatabase = string.Empty;

    public string SelectedDriverMessage => string.IsNullOrWhiteSpace(SelectedDatabase) ? "No ODBC data source selected.": $"Selected ODBC data source: {SelectedDatabase}";
    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private bool _keepCredentials;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [RelayCommand]
    private void OpenOdbcAdministrator()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "odbcad32.exe",
                UseShellExecute = true
            });

            StatusMessage = "ODBC Data Source Administrator opened.";
        }
        catch (Exception ex)
        {
            StatusMessage =
                $"Could not open ODBC Administrator: {ex.Message}";
        }
    }
    private void LoadOdbcDataSources()
    {
        try
        {
            DatabaseOptions.Clear();

            LoadDataSourcesFromRegistry(
                Registry.CurrentUser,
                @"Software\ODBC\ODBC.INI\ODBC Data Sources");

            LoadDataSourcesFromRegistry(
                Registry.LocalMachine,
                @"Software\ODBC\ODBC.INI\ODBC Data Sources");

            if (DatabaseOptions.Count > 0)
            {
                SelectedDatabase = DatabaseOptions[0];

                StatusMessage = $"{DatabaseOptions.Count} ODBC data source(s) found.";
            }
            else
            {
                StatusMessage = "No ODBC data sources were found. Create one using ODBC Data Source Administrator.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not load ODBC data sources: {ex.Message}";
        }
    }

    private void LoadDataSourcesFromRegistry(
        RegistryKey rootKey,
        string registryPath)
    {
        using var key = rootKey.OpenSubKey(registryPath);

        if (key is null)
            return;

        foreach (var valueName in key.GetValueNames())
        {
            if (string.IsNullOrWhiteSpace(valueName))
                continue;

            if (!DatabaseOptions.Contains(valueName))
            {
                DatabaseOptions.Add(valueName);
            }
        }
    }

    [RelayCommand]
    private void TestDatabaseConnection()
    {
        if (string.IsNullOrWhiteSpace(SelectedDatabase))
        {
            StatusMessage = "Please select an ODBC data source.";

            return;
        }

        StatusMessage = "Testing database connection...";

        try
        {
            var connectionString = $"DSN={SelectedDatabase};";

            using var connection =
                new OdbcConnection(connectionString);

            connection.Open();

            if (connection.State == System.Data.ConnectionState.Open)
            {
                StatusMessage = $"Connection successful: {SelectedDatabase}";
            }
            else
            {
                StatusMessage = "Connection could not be opened.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Connection failed: {ex.Message}";
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
        if (string.IsNullOrWhiteSpace(SelectedDatabase))
        {
            StatusMessage = "Please select an ODBC data source.";

            return;
        }

        Connection = new DatabaseConnection
        {
            Name = SelectedDatabase,
            Type = DatabaseType.ODBC,
            ConnectionString = $"DSN={SelectedDatabase};",
            Username = Username,
            Password = Password,
            KeepCredentials = KeepCredentials,
            IsConnected = true
        };

        StatusMessage = $"ODBC connection '{SelectedDatabase}' is ready.";

        RequestClose(Connection);
    }

    [RelayCommand]
    private void Cancel()
    {
        RequestClose(null);
    }
}
