using System.Collections.Generic;
using System.Threading.Tasks;

namespace InfoID.Desktop.Features.CardDesigner.Services;

/// <summary>
/// Tracks recently opened/saved local .infoid files so file-backed designs (Part:
/// "import a .infoid file, edits/saves go back to that file, not a new database card")
/// can still show up in Recent Cards -- they have no Template row for
/// ICardDesignRepository.GetRecentAsync to find. JSON-file-backed, same pattern as
/// JsonUserPreferencesService: no database schema change for something that's really
/// just a small local cache of file paths.
/// </summary>
public interface IRecentFilesService
{
    /// <summary>Records that this file was just opened/saved, moving it to the front of
    /// the list (or adding it if new). Called from Import and from every Save of a
    /// file-backed document.</summary>
    Task TrackAsync(string filePath, string name);

    /// <summary>Most-recently-opened first. Entries whose file no longer exists are
    /// silently dropped by the caller (RecentCardService), not here -- this service only
    /// tracks what was opened, it doesn't validate the filesystem on every read.</summary>
    IReadOnlyList<RecentFileEntry> GetRecentFiles(int maxCount = 20);

    /// <summary>"Remove from Recent" -- does NOT delete the actual file. Deleting a
    /// user's file on disk just because they right-clicked "Delete" in a recent-items
    /// list would be a surprising, destructive action for something the app doesn't
    /// own; this only forgets it was ever opened here.</summary>
    Task RemoveAsync(string filePath);

    Task SetPinnedAsync(string filePath, bool pinned);
}
