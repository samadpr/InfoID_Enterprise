using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InfoID.Desktop.Core.Dialogs;
using InfoID.Desktop.Features.Database.Models;
using InfoID.Desktop.Features.Database.Services;
using System;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using System.Collections.Generic;
namespace InfoID.Desktop.Features.Database.ViewModels;
using InfoID.Desktop.Core.Services;

public sealed partial class MySqlConnectionDialogViewModel
    : DialogViewModelBase<DatabaseConnection?>
{
    public IReadOnlyList<string> SslConnectionOptions { get; } =
    new[]
    {
        "No",
        "Yes"
    };

    public IReadOnlyList<string> ConnectionMethodOptions { get; } =
        new[]
        {
        "Standard TCP/IP",
        "Standard TCP/IP over SSH"
        };

    public ObservableCollection<string> Databases { get; } = new();
    private readonly MySqlDatabaseService _mySqlDatabaseService;
    private readonly IFilePickerService _filePickerService;
    private readonly MySqlSshTunnelService _mySqlSshTunnelService;

    [ObservableProperty]
    private string _server = "localhost";

    [ObservableProperty]
    private string _port = "3306";

    [ObservableProperty]
    private string _database = string.Empty;

    [ObservableProperty]
    private string _username = "root";

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private string _sslConnection = "No";

    [ObservableProperty]
    private string _connectionMethod = "Standard TCP/IP";

    [ObservableProperty]
    private bool _isSshConnection;

    [ObservableProperty]
    private bool _useSshKeyFile;

    partial void OnConnectionMethodChanged(string value)
    {
        IsSshConnection =
            value == "Standard TCP/IP over SSH";
    }

    partial void OnUseSshPasswordChanged(bool value)
    {
        if (value)
            UseSshKeyFile = false;
    }

    partial void OnUseSshKeyFileChanged(bool value)
    {
        if (value)
            UseSshPassword = false;
    }
    [ObservableProperty]
    private string _sshHostname = string.Empty;

    [ObservableProperty]
    private string _sshUsername = string.Empty;

    [ObservableProperty]
    private string _sshPassword = string.Empty;

    [ObservableProperty]
    private string _sshKeyFile = string.Empty;

    [ObservableProperty]
    private bool _useSshPassword = true;

    [ObservableProperty]
    private bool _keepCredentials;

    [ObservableProperty]
    private string _selectedDatabase = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public MySqlConnectionDialogViewModel(
    IFilePickerService filePickerService, MySqlSshTunnelService mySqlSshTunnelService)
    {
        _filePickerService =
            filePickerService;

        _mySqlSshTunnelService =
       mySqlSshTunnelService;

        _mySqlDatabaseService =
            new MySqlDatabaseService();
    }

    [RelayCommand]
    private async Task BrowseSshKeyFile()
    {
        var path =
            await _filePickerService.PickOpenFileAsync(
                "Select SSH Private Key",
                "SSH Key Files",
                new[]
                {
                "*.pem",
                "*.key",
                "*.*"
                });

        if (string.IsNullOrWhiteSpace(path))
            return;

        SshKeyFile = path;
    }
    [RelayCommand]
    private void TestConnection()
    {
        try
        {
            Databases.Clear();
            SelectedDatabase = string.Empty;

            ObservableCollection<string> databases;
            string sslCipher = string.Empty;

            if (ConnectionMethod == "Standard TCP/IP over SSH")
            {
                if (string.IsNullOrWhiteSpace(SshHostname))
                {
                    StatusMessage =
                        "SSH Hostname is required.";

                    return;
                }

                if (string.IsNullOrWhiteSpace(SshUsername))
                {
                    StatusMessage =
                        "SSH Username is required.";

                    return;
                }

                if (UseSshPassword)
                {
                    if (string.IsNullOrWhiteSpace(SshPassword))
                    {
                        StatusMessage =
                            "SSH Password is required.";

                        return;
                    }

                    _mySqlSshTunnelService
                        .StartPasswordTunnel(
                            SshHostname,
                            SshUsername,
                            SshPassword,
                            Server,
                            uint.Parse(Port));
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(SshKeyFile))
                    {
                        StatusMessage =
                            "SSH Key File is required.";

                        return;
                    }

                    _mySqlSshTunnelService
                        .StartKeyFileTunnel(
                            SshHostname,
                            SshUsername,
                            SshKeyFile,
                            Server,
                            uint.Parse(Port));
                }

                var sshTestConnectionString =
     _mySqlDatabaseService
         .BuildConnectionString(
             "127.0.0.1",
             _mySqlSshTunnelService.LocalPort.ToString(),
             "mysql",
             Username,
             Password,
             SslConnection == "Yes");

                sslCipher =
                    _mySqlDatabaseService
                        .TestConnection(sshTestConnectionString);

                databases =
                    _mySqlDatabaseService
                        .GetDatabases(
                            "127.0.0.1",
                            _mySqlSshTunnelService.LocalPort.ToString(),
                            Username,
                            Password,
                            SslConnection == "Yes");
            }
            else
            {
                _mySqlSshTunnelService.Stop();

                var testConnectionString =
    _mySqlDatabaseService
        .BuildConnectionString(Server, Port, "mysql",Username, Password, SslConnection == "Yes");

                sslCipher = _mySqlDatabaseService.TestConnection(testConnectionString);

                StatusMessage =
                    SslConnection == "Yes"
                        ? $"MySQL connection successful. TLS: {sslCipher}"
                        : "MySQL connection successful. TLS: Not enabled.";
                databases =
                    _mySqlDatabaseService
                        .GetDatabases(
                            Server,
                            Port,
                            Username,
                            Password,
                            SslConnection == "Yes");
            }

            foreach (var database in databases)
            {
                Databases.Add(database);
            }

            if (Databases.Count == 0)
            {
                StatusMessage =
                    "No MySQL databases were found.";

                return;
            }

            SelectedDatabase =
                Databases[0];

            if (SslConnection == "Yes" &&
    !string.IsNullOrWhiteSpace(sslCipher))
            {
                StatusMessage =
                    $"{Databases.Count} database(s) found. TLS: {sslCipher}";
            }
            else if (SslConnection == "Yes")
            {
                StatusMessage =
                    $"{Databases.Count} database(s) found. TLS is not active.";
            }
            else
            {
                StatusMessage =
                    $"{Databases.Count} database(s) found. TLS: Not enabled.";
            }
        }
        catch (FormatException)
        {
            StatusMessage =
                "Port must be a valid number.";
        }
        catch (Exception ex)
        {
            _mySqlSshTunnelService.Stop();

            StatusMessage =
                $"Connection failed: {ex.Message}";
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
            string connectionServer;
            string connectionPort;

            if (ConnectionMethod == "Standard TCP/IP over SSH")
            {
                if (!_mySqlSshTunnelService
                        .LocalPort
                        .Equals(0))
                {
                    connectionServer = "127.0.0.1";
                    connectionPort =
                        _mySqlSshTunnelService
                            .LocalPort
                            .ToString();
                }
                else
                {
                    StatusMessage =
                        "Please test the SSH connection first.";

                    return;
                }
            }
            else
            {
                connectionServer = Server;
                connectionPort = Port;
            }

            var connectionString =
                _mySqlDatabaseService
                    .BuildConnectionString(
                        connectionServer,
                        connectionPort,
                        SelectedDatabase,
                        Username,
                        Password,
                        SslConnection == "Yes");

            _mySqlDatabaseService
                .TestConnection(connectionString);

            var connection =
                new DatabaseConnection
                {
                    Name = SelectedDatabase,
                    Type = DatabaseType.MySQL,
                    ConnectionString = connectionString,
                    Username = Username,
                    Password = Password,
                    KeepCredentials = KeepCredentials,
                    IsConnected = true
                };

            RequestClose(connection);
        }
        catch (FormatException)
        {
            StatusMessage =
                "Port must be a valid number.";
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
