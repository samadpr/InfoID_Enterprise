using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using InfoID.Infrastructure.Persistence;

namespace InfoID.Desktop.Core.Services;

/// <summary>
/// Default <see cref="IUserPreferencesService"/>. Loads Preferences.json synchronously
/// once at construction (registered as a DI singleton, so this only happens once per
/// app run, and the file is tiny) and writes it back atomically (temp file + copy, same
/// pattern as JsonCustomCardModelStore) whenever a value changes.
/// </summary>
public sealed class JsonUserPreferencesService : IUserPreferencesService
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public UserPreferences Current { get; private set; }

    public event EventHandler? PreferencesChanged;

    public JsonUserPreferencesService()
    {
        _filePath = Path.Combine(DatabaseLocation.GetApplicationRoot(), "Preferences.json");
        Current = LoadFromDisk();
    }

    public async Task SetAutosaveEnabledAsync(bool enabled)
    {
        await _lock.WaitAsync();
        try
        {
            Current = new UserPreferences { AutosaveEnabled = enabled };
            await WriteAllAsync(Current);
        }
        finally
        {
            _lock.Release();
        }

        PreferencesChanged?.Invoke(this, EventArgs.Empty);
    }

    private UserPreferences LoadFromDisk()
    {
        if (!File.Exists(_filePath))
        {
            return new UserPreferences();
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<UserPreferences>(json) ?? new UserPreferences();
        }
        catch
        {
            // Corrupt/partially-written preferences file must never stop the app from
            // starting -- fall back to defaults rather than throwing out of a DI
            // singleton constructor.
            return new UserPreferences();
        }
    }

    private async Task WriteAllAsync(UserPreferences preferences)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(preferences, options);
        var tempPath = _filePath + ".tmp";
        await File.WriteAllTextAsync(tempPath, json);
        File.Copy(tempPath, _filePath, overwrite: true);
        File.Delete(tempPath);
    }
}
