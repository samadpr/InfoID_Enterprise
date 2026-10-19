using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using InfoID.Desktop.Features.Database.Services;

namespace InfoID.Desktop.Features.Visitors.Services;

/// <summary>A bulk-upload file read into a table (every value as text).</summary>
public sealed class VisitorFileData
{
    public required string FilePath { get; init; }
    public required DataTable Table { get; init; }
    /// <summary>Excel only: the sheets in the workbook, and the one that was read.</summary>
    public IReadOnlyList<string> Sheets { get; init; } = Array.Empty<string>();
    public string? Sheet { get; init; }

    public string FileName => Path.GetFileName(FilePath);
    public string Folder => Path.GetDirectoryName(FilePath) ?? string.Empty;
    public IReadOnlyList<string> Columns => Table.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToList();
}

/// <summary>
/// Reads visitor bulk-upload files with the readers InfoID already has for data sources:
/// Excel (ODBC, read-only, like the Excel data source), CSV/TXT, and JSON (an array of
/// visitor objects, or an object holding one such array, e.g. { "visitors": [ … ] }).
/// Blank rows are dropped; every value is returned as text for the import review.
/// </summary>
public sealed class VisitorFileReader
{
    public static readonly IReadOnlyList<string> SupportedPatterns = new[] { "*.xlsx", "*.xls", "*.xlsm", "*.csv", "*.txt", "*.json" };

    /// <summary>Largest file read (JSON is parsed in memory).</summary>
    private const long MaxFileBytes = 50L * 1024 * 1024;

    public Task<VisitorFileData> ReadAsync(string filePath, string? sheet = null) =>
        Task.Run(() => Read(filePath, sheet));

    private static VisitorFileData Read(string filePath, string? sheet)
    {
        var info = new FileInfo(filePath);
        if (!info.Exists) throw new FileNotFoundException("The file could not be found.", filePath);
        if (info.Length > MaxFileBytes) throw new InvalidOperationException("The file is larger than 50 MB. Split it into smaller files.");

        var extension = info.Extension.ToLowerInvariant();
        switch (extension)
        {
            case ".csv":
            case ".txt":
                return new VisitorFileData { FilePath = filePath, Table = AsText(new CsvTextDatabaseService().LoadFile(filePath)) };

            case ".json":
                var json = File.ReadAllText(filePath);
                DataTable table;
                try
                {
                    table = new ApiDatabaseService().ConvertJsonToDataTable(json);
                }
                catch (System.Text.Json.JsonException ex)
                {
                    throw new InvalidOperationException("This isn't a valid JSON file: " + ex.Message, ex);
                }
                return new VisitorFileData { FilePath = filePath, Table = AsText(table) };

            case ".xlsx":
            case ".xls":
            case ".xlsm":
                return ReadExcel(filePath, sheet);

            default:
                throw new NotSupportedException("Choose an Excel, CSV, TXT or JSON file.");
        }
    }

    private static VisitorFileData ReadExcel(string filePath, string? sheet)
    {
        // Same read-only ODBC connection as the Excel data source (ExcelConnectionDialogViewModel).
        var connectionString =
            "Driver={Microsoft Excel Driver (*.xls, *.xlsx, *.xlsm, *.xlsb)};" +
            $"DBQ={filePath};" +
            "ReadOnly=1;";
        var excel = new ExcelOdbcService();

        DataTable schema;
        try
        {
            schema = excel.GetSheets(connectionString);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "The Excel file couldn't be opened. Close it in Excel and try again, or save it as CSV. (" + ex.Message + ")", ex);
        }

        var sheets = schema.Rows.Cast<DataRow>()
            .Select(r => r["TABLE_NAME"]?.ToString())
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!)
            // Real worksheets end with '$'; named ranges and filters don't.
            .Where(n => n.TrimEnd('\'').EndsWith('$'))
            .ToList();
        if (sheets.Count == 0) throw new InvalidOperationException("The Excel file has no worksheets.");

        var chosen = sheet is not null && sheets.Contains(sheet) ? sheet : sheets[0];
        var table = excel.ReadSheet(connectionString, chosen);
        return new VisitorFileData { FilePath = filePath, Table = AsText(table), Sheets = sheets, Sheet = chosen };
    }

    /// <summary>Copies the table with every value as trimmed text (dates as ISO) and gives
    /// unnamed columns a name. Empty rows are kept here (so row numbers match the file) and
    /// skipped by the import review.</summary>
    internal static DataTable AsText(DataTable source)
    {
        var result = new DataTable(source.TableName);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (DataColumn column in source.Columns)
        {
            var name = string.IsNullOrWhiteSpace(column.ColumnName) ? $"Column {result.Columns.Count + 1}" : column.ColumnName.Trim();
            var unique = name;
            for (var n = 2; !used.Add(unique); n++) unique = $"{name} ({n})";
            result.Columns.Add(unique, typeof(string));
        }

        foreach (DataRow row in source.Rows)
        {
            var values = new object[source.Columns.Count];
            for (var i = 0; i < source.Columns.Count; i++)
            {
                var text = row[i] switch
                {
                    null or DBNull => string.Empty,
                    DateTime d => d.TimeOfDay == TimeSpan.Zero
                        ? d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
                        : d.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
                    IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
                    var other => other.ToString() ?? string.Empty,
                };
                values[i] = text.Trim();
            }
            result.Rows.Add(values);
        }
        return result;
    }
}
