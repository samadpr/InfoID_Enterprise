using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using InfoID.Desktop.Features.Printing.Models;
using InfoID.Infrastructure.Persistence;

namespace InfoID.Desktop.Features.Printing.Services;

/// <summary>The Print dialog's remembered choices: printer and output settings the user
/// normally sets once and reuses every time. Deliberately NOT the print range (which
/// records to print), which is decided per run.</summary>
public sealed class PrintSettings
{
    public string? PrinterName { get; set; }
    public bool ShowSystemPrintDialog { get; set; }
    public bool PrintFront { get; set; } = true;
    public bool PrintBack { get; set; } = true;
    public int NumberOfCopies { get; set; } = 1;
    public bool ReversedOrder { get; set; }
    // Full colour by default (new installs only -- a saved PrintSettings.json keeps the
    // operator's choice). MonoChrome is for black-panel-only resin printing.
    public PrintColorMode ColorMode { get; set; } = PrintColorMode.Composite;
    public PrintAntialiasMode AntialiasMode { get; set; } = PrintAntialiasMode.OnlyImages;
    public double BlackPanelThreshold { get; set; } = 128;
    public bool Rotate180 { get; set; }
    public bool Rotate180Landscape { get; set; }
    public bool Rotate180Portrait { get; set; }
    public DigitalCopyMode DigitalCopyMode { get; set; } = DigitalCopyMode.Off;
    public PageParityFilter PageParity { get; set; } = PageParityFilter.AllPages;
}

/// <summary>Persists <see cref="PrintSettings"/> to PrintSettings.json in InfoID's app
/// folder -- same local-JSON-file idiom as JsonPrintStatusStore. One shared instance
/// (like PhotoFolderService.Shared) so it needs no DI or constructor changes; can move
/// to DI registration later without changing callers.</summary>
public sealed class PrintSettingsStore
{
    private static readonly InfoID.Shared.Logging.AppLogger Log = InfoID.Shared.Logging.AppLog.For<PrintSettingsStore>();

    public static PrintSettingsStore Shared { get; } = new();

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _filePath;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private PrintSettingsStore()
    {
        _filePath = Path.Combine(DatabaseLocation.GetApplicationRoot(), "PrintSettings.json");
    }

    public async Task<PrintSettings> LoadAsync()
    {
        if (!File.Exists(_filePath)) return new PrintSettings();

        try
        {
            await using var stream = File.OpenRead(_filePath);
            return await JsonSerializer.DeserializeAsync<PrintSettings>(stream, Options) ?? new PrintSettings();
        }
        catch
        {
            // Corrupt or partly written file: fall back to defaults rather than stop
            // the Print dialog from opening.
            return new PrintSettings();
        }
    }

    public async Task SaveAsync(PrintSettings settings)
    {
        await _writeLock.WaitAsync();
        try
        {
            var tempPath = _filePath + ".tmp";
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, settings, Options);
            }
            File.Copy(tempPath, _filePath, overwrite: true);
            File.Delete(tempPath);
        }
        catch (Exception ex)
        {
            // Remembering settings is a convenience; never fail a print over it.
            Log.Warn("Print settings save error", ex);
        }
        finally
        {
            _writeLock.Release();
        }
    }
}
