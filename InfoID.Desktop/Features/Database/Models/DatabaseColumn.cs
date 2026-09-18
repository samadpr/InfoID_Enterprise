namespace InfoID.Desktop.Features.Database.Models;

public sealed class DatabaseColumn
{
    public string Name { get; set; } = string.Empty;

    public string DataType { get; set; } = string.Empty;

    public bool IsPrimaryKey { get; set; }

    public bool IsSelected { get; set; } = true;
}
