using System;

namespace InfoID.Desktop.Features.Database.Models;

public sealed class DatabaseConnection
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string Name { get; set; } = string.Empty;

    public DatabaseType Type { get; set; }

    public string ConnectionString { get; set; } = string.Empty;

    public bool IsConnected { get; set; }
}