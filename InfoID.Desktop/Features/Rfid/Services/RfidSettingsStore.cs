using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using InfoID.CardEncoding.Contactless;
using InfoID.Infrastructure.Persistence;

namespace InfoID.Desktop.Features.Rfid.Services;

/// <summary>This PC's card-encoding setup. Belongs to the PC (the reader plugged into it),
/// not to designs or data.</summary>
public sealed class RfidSettings
{
    /// <summary>PC/SC name of the contactless reader, e.g. "HID OMNIKEY 5427 CK CL 0".</summary>
    public string? ReaderName { get; set; }

    public RfidSettings Clone() => (RfidSettings)MemberwiseClone();
}

/// <summary>RfidSettings.json in InfoID's data folder (same local-JSON idiom as BackupSettingsStore).</summary>
public sealed class RfidSettingsStore
{
    private static readonly InfoID.Shared.Logging.AppLogger Log = InfoID.Shared.Logging.AppLog.For<RfidSettingsStore>();
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _filePath;
    private readonly object _gate = new();
    private RfidSettings? _current;

    public RfidSettingsStore()
        : this(Path.Combine(DatabaseLocation.GetApplicationRoot(), "RfidSettings.json"))
    {
    }

    public RfidSettingsStore(string filePath) => _filePath = filePath;

    /// <summary>A copy of the current settings.</summary>
    public RfidSettings Current
    {
        get
        {
            lock (_gate)
            {
                _current ??= Load();
                return _current.Clone();
            }
        }
    }

    /// <summary>Changes the settings and saves them (never throws).</summary>
    public void Update(Action<RfidSettings> change)
    {
        lock (_gate)
        {
            var next = (_current ??= Load()).Clone();
            change(next);
            _current = next;
            try
            {
                var tempPath = _filePath + ".tmp";
                File.WriteAllText(tempPath, JsonSerializer.Serialize(next, Options));
                File.Copy(tempPath, _filePath, overwrite: true);
                File.Delete(tempPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warn("Couldn't save the card encoding settings", ex);
            }
        }
    }

    /// <summary>At start-up: watch the saved reader again if it's plugged in, so encoding
    /// works without opening the settings page first. Runs in the background; never throws.</summary>
    public static Task SelectSavedReaderAsync(IContactlessReaderService readers, RfidSettingsStore settings) =>
        Task.Run(() =>
        {
            var name = settings.Current.ReaderName;
            if (string.IsNullOrWhiteSpace(name)) return;
            try
            {
                if (readers.GetReaders().Contains(name)) readers.SelectReader(name);
            }
            catch (ContactlessException ex)
            {
                Log.Warn("The saved card reader couldn't be selected at start-up", ex);
            }
        });

    private RfidSettings Load()
    {
        try
        {
            if (File.Exists(_filePath))
                return JsonSerializer.Deserialize<RfidSettings>(File.ReadAllText(_filePath)) ?? new RfidSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Log.Warn("Couldn't read the card encoding settings; using defaults", ex);
        }
        return new RfidSettings();
    }
}
