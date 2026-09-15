using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using InfoID.Infrastructure.Persistence;

namespace InfoID.Desktop.Features.CardDesigner.Services;

/// <summary>
/// Tracks the most-recently-used font family names for the Font family picker's
/// "recent fonts first" ordering, and persists them across app restarts.
///
/// A plain static class (like SystemFontCatalog, its natural neighbor here) rather than
/// a DI-injected service, because the thing that actually changes FontFamily --
/// TextElement/DataFieldElement's OnFontFamilyChanged partial method -- lives on a
/// model class with the DataTemplate bound directly to it (there's no per-element
/// ViewModel in this codebase to inject a service into); a plain static call is the
/// simplest way to record a change no matter which code path made it. Persistence uses
/// its own small JSON file (RecentFonts.json under DatabaseLocation.GetApplicationRoot,
/// the same root JsonRecentFilesService and JsonUserPreferencesService already write
/// under) rather than a shared file, since this data has nothing to do with either of
/// those.
/// </summary>
public static class RecentFontsTracker
{
    private const int MaxRecent = 8;

    private static readonly string FilePath =
        Path.Combine(DatabaseLocation.GetApplicationRoot(), "RecentFonts.json");

    private static readonly object Lock = new();
    private static List<string> _recent = Load();

    /// <summary>Most-recently-used first. Not filtered against what's actually
    /// installed on this machine -- callers building a picker list (see
    /// SystemFontCatalog.FontFamilyNamesWithRecentFirst) are responsible for that.</summary>
    public static IReadOnlyList<string> Recent
    {
        get { lock (Lock) return _recent.ToList(); }
    }

    /// <summary>Moves <paramref name="fontFamilyName"/> to the front of the recent
    /// list (adding it if new), capped at MaxRecent. Called from TextElement's and
    /// DataFieldElement's OnFontFamilyChanged -- so every code path that ever sets a
    /// FontFamily is tracked, not just the one Properties-panel ComboBox.</summary>
    public static void Record(string? fontFamilyName)
    {
        if (string.IsNullOrWhiteSpace(fontFamilyName)) return;

        lock (Lock)
        {
            _recent.RemoveAll(f => string.Equals(f, fontFamilyName, StringComparison.OrdinalIgnoreCase));
            _recent.Insert(0, fontFamilyName);
            if (_recent.Count > MaxRecent) _recent.RemoveRange(MaxRecent, _recent.Count - MaxRecent);
            Save();
        }
    }

    private static List<string> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new List<string>();
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch
        {
            // A missing/corrupt recent-fonts file must never block opening the
            // Properties panel -- worst case, "recent" just starts out empty again.
            return new List<string>();
        }
    }

    private static void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_recent));
        }
        catch
        {
            // Best-effort persistence -- a failed write here shouldn't disrupt editing.
        }
    }
}
