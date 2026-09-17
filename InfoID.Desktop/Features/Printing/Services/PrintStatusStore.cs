using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using InfoID.Desktop.Features.Printing.Models;
using InfoID.Infrastructure.Persistence;

namespace InfoID.Desktop.Features.Printing.Services;

/// <summary>Persists per-record print status (Advanced Print Operations tab) across app
/// restarts -- same local-JSON-file idiom as JsonUserPreferencesService/
/// JsonRecentFilesService/JsonCustomCardModelStore elsewhere in this codebase, keeping
/// this machine-local like everything else here until a real database exists.</summary>
public interface IPrintStatusStore
{
    Task<IReadOnlyDictionary<string, PrintRecordStatus>> LoadAsync();
    Task SaveAsync(IReadOnlyDictionary<string, PrintRecordStatus> statuses);
}

public sealed class JsonPrintStatusStore : IPrintStatusStore
{
    private readonly string _filePath;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public JsonPrintStatusStore()
    {
        _filePath = Path.Combine(DatabaseLocation.GetApplicationRoot(), "PrintStatus.json");
    }

    public async Task<IReadOnlyDictionary<string, PrintRecordStatus>> LoadAsync()
    {
        if (!File.Exists(_filePath)) return new Dictionary<string, PrintRecordStatus>();

        try
        {
            await using var stream = File.OpenRead(_filePath);
            var loaded = await JsonSerializer.DeserializeAsync<Dictionary<string, PrintRecordStatus>>(stream, Options);
            return loaded ?? new Dictionary<string, PrintRecordStatus>();
        }
        catch
        {
            // Corrupt/partially-written file -- start clean rather than crash the
            // Print dialog from opening at all.
            return new Dictionary<string, PrintRecordStatus>();
        }
    }

    public async Task SaveAsync(IReadOnlyDictionary<string, PrintRecordStatus> statuses)
    {
        var tempPath = _filePath + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, statuses, Options);
        }
        File.Copy(tempPath, _filePath, overwrite: true);
        File.Delete(tempPath);
    }
}
