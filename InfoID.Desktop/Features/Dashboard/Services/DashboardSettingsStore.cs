using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using InfoID.Infrastructure.Persistence;

namespace InfoID.Desktop.Features.Dashboard.Services;

/// <summary>Which Dashboard widgets are shown (FR-DSH-4). All on by default.</summary>
public sealed class DashboardSettings
{
    public bool ShowMetrics { get; set; } = true;
    public bool ShowTrend { get; set; } = true;
    public bool ShowExpiring { get; set; } = true;
    public bool ShowRecent { get; set; } = true;
    public bool ShowHealth { get; set; } = true;
    public bool ShowShortcuts { get; set; } = true;
}

/// <summary>Persists <see cref="DashboardSettings"/> to DashboardSettings.json in InfoID's
/// app folder -- same local-JSON idiom and shared-instance pattern as PrintSettingsStore.</summary>
public sealed class DashboardSettingsStore
{
    private static readonly InfoID.Shared.Logging.AppLogger Log = InfoID.Shared.Logging.AppLog.For<DashboardSettingsStore>();

    public static DashboardSettingsStore Shared { get; } = new();

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private DashboardSettingsStore()
    {
        _filePath = Path.Combine(DatabaseLocation.GetApplicationRoot(), "DashboardSettings.json");
    }

    public async Task<DashboardSettings> LoadAsync()
    {
        if (!File.Exists(_filePath)) return new DashboardSettings();
        try
        {
            await using var stream = File.OpenRead(_filePath);
            return await JsonSerializer.DeserializeAsync<DashboardSettings>(stream, Options) ?? new DashboardSettings();
        }
        catch
        {
            return new DashboardSettings();
        }
    }

    public async Task SaveAsync(DashboardSettings settings)
    {
        await _lock.WaitAsync();
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
            Log.Warn("Dashboard settings save error", ex);
        }
        finally
        {
            _lock.Release();
        }
    }
}
