namespace InfoID.Desktop.Features.CardDesigner.Models.Document;

public sealed class DatabaseField
{
    public string DatabaseName { get; set; } = string.Empty;

    public string TableName { get; set; } = string.Empty;

    public string ColumnName { get; set; } = string.Empty;

    public string DisplayName =>
        $"{TableName}.{ColumnName}";
}
