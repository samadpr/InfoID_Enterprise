using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Text;
using System.Linq;

namespace InfoID.Desktop.Features.Database.Services;

public sealed class CsvTextDatabaseService
{
    public DataTable LoadFile(string filePath,string encodingOption = "AUTO",string delimiterOption = "AUTO")
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path is required.");

        if (!File.Exists(filePath))
            throw new FileNotFoundException(
                "The selected file could not be found.",
                filePath);

        var extension =
            Path.GetExtension(filePath).ToLowerInvariant();

        if (extension != ".csv" && extension != ".txt")
        {
            throw new NotSupportedException(
                "Only CSV and TXT files are supported.");
        }

        return ReadDelimitedFile(filePath, encodingOption,
    delimiterOption);
    }

    private DataTable ReadDelimitedFile(string filePath, string encodingOption,
    string delimiterOption)
    {
        var table = new DataTable();

        var encoding = GetEncoding( filePath,encodingOption);
        using var reader = new StreamReader(
            filePath,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true);

        var headerLine = reader.ReadLine();

        if (string.IsNullOrWhiteSpace(headerLine))
            return table;

        var delimiter = GetDelimiter(headerLine, delimiterOption);

        var headers = ParseLine(headerLine, delimiter);

        foreach (var header in headers)
        {
            var columnName = string.IsNullOrWhiteSpace(header)
                ? $"Column{table.Columns.Count + 1}"
                : header.Trim();

            var uniqueName = columnName;
            var counter = 2;

            while (table.Columns.Contains(uniqueName))
            {
                uniqueName = $"{columnName}_{counter++}";
            }

            table.Columns.Add(uniqueName);
        }

        string? line;

        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var values = ParseLine(line, delimiter);

            var row = table.NewRow();

            for (var i = 0;
                 i < table.Columns.Count && i < values.Count;
                 i++)
            {
                row[i] = values[i];
            }

            table.Rows.Add(row);
        }

        return table;
    }
    private static Encoding GetEncoding(
    string filePath,
    string encodingOption)
    {
        if (encodingOption == "UTF-8")
            return new UTF8Encoding(false);

      

        if (encodingOption == "UTF-16 LE")
            return new UnicodeEncoding(
                false,
                true);

        if (encodingOption == "UTF-16 BE")
            return new UnicodeEncoding(
                true,
                true);

     

        // AUTO
        using var stream =
            File.OpenRead(filePath);

        Span<byte> bom =
            stackalloc byte[4];

        var bytesRead =
            stream.Read(bom);

        // UTF-8 BOM
        if (bytesRead >= 3 &&
            bom[0] == 0xEF &&
            bom[1] == 0xBB &&
            bom[2] == 0xBF)
        {
            return new UTF8Encoding(false);
        }

        // UTF-16 LE BOM
        if (bytesRead >= 2 &&
            bom[0] == 0xFF &&
            bom[1] == 0xFE)
        {
            return new UnicodeEncoding(
                false,
                true);
        }

        // UTF-16 BE BOM
        if (bytesRead >= 2 &&
            bom[0] == 0xFE &&
            bom[1] == 0xFF)
        {
            return new UnicodeEncoding(
                true,
                true);
        }

        return new UTF8Encoding(false);
    }
    private static char GetDelimiter(
    string line,
    string delimiterOption)
    {
        return delimiterOption switch
        {
            "Comma (,)" => ',',
            "Semicolon (;)" => ';',
            "Tab" => '\t',
            "Pipe (|)" => '|',

            _ => DetectDelimiter(line)
        };
    }

    private static char DetectDelimiter(string line)
    {
        var candidates = new[]
        {
        ',',
        ';',
        '\t',
        '|'
    };

        return candidates
            .OrderByDescending(
                delimiter =>
                    line.Count(
                        character =>
                            character == delimiter))
            .FirstOrDefault(',');
    }

    private static List<string> ParseLine(
        string line,
        char delimiter)
    {
        var values = new List<string>();
        var current = new StringBuilder();
        var insideQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var character = line[i];

            if (character == '"')
            {
                if (insideQuotes &&
                    i + 1 < line.Length &&
                    line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                    continue;
                }

                insideQuotes = !insideQuotes;
                continue;
            }

            if (character == delimiter && !insideQuotes)
            {
                values.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(character);
        }

        values.Add(current.ToString());

        return values;
    }
}
