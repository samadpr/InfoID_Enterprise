using System;
using System.Collections.Generic;

namespace InfoID.Desktop.Features.Database.Models;

public sealed class DatabaseConnection
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string Name { get; set; } = string.Empty;

    public DatabaseType Type { get; set; }

    public string ConnectionString { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public bool KeepCredentials { get; set; }
    public string SelectedTable { get; set; } = string.Empty;
    public List<DatabaseColumn> SelectedColumns { get; set; } = new();
    public bool IsConnected { get; set; }
    public string AuthenticationType { get; set; } = "None";
    public string ApiKey { get; set; } = string.Empty;
    public string BearerToken { get; set; } = string.Empty;
    public string CredentialsFilePath { get; set; } = string.Empty;
}
