using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using InfoID.Desktop.Features.Printing.Models;
using InfoID.Infrastructure.Persistence;

namespace InfoID.Desktop.Features.Printing.Services;

/// <summary>Remembers <see cref="PrintHistorySettings"/> per design (keyed by
/// PrintHistoryPlanner.BuildDesignKey) in PrintHistorySettings.json in InfoID's app
/// folder -- same local-JSON idiom and shared-instance pattern as PrintSettingsStore.</summary>
public sealed class PrintHistorySettingsStore
{
    private static readonly InfoID.Shared.Logging.AppLogger Log = InfoID.Shared.Logging.AppLog.For<PrintHistorySettingsStore>();

    public static PrintHistorySettingsStore Shared { get; } = new();

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private PrintHistorySettingsStore()
    {
        _filePath = Path.Combine(DatabaseLocation.GetApplicationRoot(), "PrintHistorySettings.json");
    }

    public async Task<PrintHistorySettings?> LoadAsync(string designKey)
    {
        await _lock.WaitAsync();
        try
        {
            var all = await ReadAllAsync();
            return all.TryGetValue(designKey, out var settings) ? settings : null;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync(string designKey, PrintHistorySettings settings)
    {
        await _lock.WaitAsync();
        try
        {
            var all = await ReadAllAsync();
            all[designKey] = settings;

            var tempPath = _filePath + ".tmp";
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, all, Options);
            }
            File.Copy(tempPath, _filePath, overwrite: true);
            File.Delete(tempPath);
        }
        catch (Exception ex)
        {
            // A convenience only; never fail a print over it.
            Log.Warn("Print history settings save error", ex);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<Dictionary<string, PrintHistorySettings>> ReadAllAsync()
    {
        if (!File.Exists(_filePath)) return new Dictionary<string, PrintHistorySettings>(StringComparer.OrdinalIgnoreCase);
        try
        {
            await using var stream = File.OpenRead(_filePath);
            var data = await JsonSerializer.DeserializeAsync<Dictionary<string, PrintHistorySettings>>(stream, Options);
            return data is null
                ? new Dictionary<string, PrintHistorySettings>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, PrintHistorySettings>(data, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, PrintHistorySettings>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
