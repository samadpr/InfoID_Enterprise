using System.Collections.Generic;

namespace InfoID.Desktop.Features.Database.Models;

public sealed class DatabaseTableDefinition
{
    public string DatabaseName { get; set; } = string.Empty;

    public string TableName { get; set; } = string.Empty;

    public string ConnectionString { get; set; } = string.Empty;

    public List<DatabaseColumn> Columns { get; set; } = new();

    public string PrimaryKey { get; set; } = string.Empty;
}
