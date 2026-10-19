using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InfoID.Desktop.Core.Dialogs;
using InfoID.Desktop.Features.Database.Models;
using InfoID.Desktop.Features.Database.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;


namespace InfoID.Desktop.Features.Database.ViewModels;

public sealed partial class CsvTextConnectionDialogViewModel
    : DialogViewModelBase<DatabaseConnection?>
{
    private readonly IFilePickerService _filePickerService;

    public override string Title => "Connect to CSV / TXT";

    [ObservableProperty]
    private string _filePath = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public IReadOnlyList<string> EncodingOptions { get; } =
    new[]
    {
        "AUTO",
        "UTF-8",
        "UTF-16 LE",
        "UTF-16 BE"
    };

    [ObservableProperty]
    private string _selectedEncoding = "AUTO";

    public IReadOnlyList<string> DelimiterOptions { get; } =
        new[]
        {
        "AUTO",
        "Comma (,)",
        "Semicolon (;)",
        "Tab",
        "Pipe (|)"
        };

    [ObservableProperty]
    private string _selectedDelimiter = "AUTO";
    public CsvTextConnectionDialogViewModel(
        IFilePickerService filePickerService)
    {
        _filePickerService = filePickerService;
    }

    [RelayCommand]
    private async Task Browse()
    {
        try
        {
            var filePath = await _filePickerService.PickOpenFileAsync(
        "Select CSV or TXT File",
        "CSV/TXT Files",
        new[]
        {
            "*.csv",
            "*.txt"
        });

            if (string.IsNullOrWhiteSpace(filePath))
                return;

            FilePath = filePath;

            var service = new CsvTextDatabaseService();

            var data = service.LoadFile(FilePath, SelectedEncoding,SelectedDelimiter);
            if (SelectedDelimiter == "AUTO")
            {
                SelectedDelimiter =
                    DetectDelimiterOption(
                        FilePath,
                        SelectedEncoding);
            }

            StatusMessage =
    $"{data.Rows.Count} row(s) loaded from " +
    $"{System.IO.Path.GetFileName(FilePath)} " +
    $"({data.Columns.Count} column(s)).";
        }
        catch (Exception ex)
        {
            StatusMessage =
                $"Could not load file: {ex.Message}";
        }
    }


    private static string DetectDelimiterOption(
    string filePath,
    string encodingOption)
    {
        var service = new CsvTextDatabaseService();

        var data = service.LoadFile(
            filePath,
            encodingOption,
            "AUTO");

        if (data.Columns.Count <= 1)
            return "AUTO";

        // Determine the delimiter by checking the first line.
        System.Text.Encoding encoding = encodingOption switch
        {
            "UTF-8" => new System.Text.UTF8Encoding(false),
            "UTF-16 LE" => new System.Text.UnicodeEncoding(false, true),
            "UTF-16 BE" => new System.Text.UnicodeEncoding(true, true),
            _ => new System.Text.UTF8Encoding(false)
        };

        using var reader = new StreamReader(
            filePath,
            encoding,
            detectEncodingFromByteOrderMarks: true);

        var headerLine = reader.ReadLine();

        if (string.IsNullOrWhiteSpace(headerLine))
            return "AUTO";

        var candidates = new Dictionary<string, char>
        {
            ["Comma (,)"] = ',',
            ["Semicolon (;)"] = ';',
            ["Tab"] = '\t',
            ["Pipe (|)"] = '|'
        };

        var detected = candidates
            .OrderByDescending(x =>
                headerLine.Count(c => c == x.Value))
            .FirstOrDefault();

        return detected.Value != '\0'
            ? detected.Key
            : "AUTO";
    }
    [RelayCommand]
    private void Previous()
    {
        RequestClose(null);
    }

    [RelayCommand]
    private void Next()
    {
        if (string.IsNullOrWhiteSpace(FilePath))
        {
            StatusMessage =
                "Please select a CSV or TXT file.";

            return;
        }

        try
        {
            var service = new CsvTextDatabaseService();

            var data = service.LoadFile(FilePath, SelectedEncoding,SelectedDelimiter);

            if (data.Columns.Count == 0)
            {
                StatusMessage =
                    "No columns were found in the file.";

                return;
            }

            var connection = new DatabaseConnection
            {
                Name = System.IO.Path.GetFileName(FilePath),
                Type = GetDatabaseType(),
                ConnectionString = FilePath,
                IsConnected = true
            };

            RequestClose(connection);
        }
        catch (Exception ex)
        {
            StatusMessage =
                $"Could not load file: {ex.Message}";
        }
    }

    private DatabaseType GetDatabaseType()
    {
        var extension =
            System.IO.Path.GetExtension(FilePath)
                .ToLowerInvariant();

        return extension == ".txt"
            ? DatabaseType.TXT
            : DatabaseType.CSV;
    }

    [RelayCommand]
    private void Cancel()
    {
        RequestClose(null);
    }
}
