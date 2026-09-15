using System;
using System.Threading.Tasks;

namespace InfoID.Desktop.Core.Services;

/// <summary>
/// Reads/writes the small persisted <see cref="UserPreferences"/> bag. JSON-file-backed
/// (consistent with the existing ICustomCardModelStore pattern), single Preferences.json
/// under the app's data root -- no database schema change needed for something this
/// small and machine-local.
/// </summary>
public interface IUserPreferencesService
{
    /// <summary>Current in-memory snapshot, already loaded at startup. Safe to read
    /// synchronously from any ViewModel constructor.</summary>
    UserPreferences Current { get; }

    /// <summary>Raised after a value changes and the new state has been persisted to
    /// disk, so any other open tab/window reflects the same preference immediately.</summary>
    event EventHandler? PreferencesChanged;

    Task SetAutosaveEnabledAsync(bool enabled);
}
