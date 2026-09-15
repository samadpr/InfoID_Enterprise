using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using InfoID.Infrastructure.Persistence;

namespace InfoID.Desktop.Features.CardDesigner.Services;

public sealed class JsonRecentFilesService : IRecentFilesService
{
    private const int MaxStored = 20;

    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private List<RecentFileEntry> _entries;

    public JsonRecentFilesService()
    {
        _filePath = Path.Combine(DatabaseLocation.GetApplicationRoot(), "RecentFiles.json");
        _entries = LoadFromDisk();
    }

    public async Task TrackAsync(string filePath, string name)
    {
        await _lock.WaitAsync();
        try
        {
            // Preserve the existing Pinned flag rather than recreating the entry from
            // scratch -- otherwise every open/save of a pinned file would silently
            // un-pin it the next time Recent Cards loads.
            var wasPinned = _entries.FirstOrDefault(e => string.Equals(e.FilePath, filePath, StringComparison.OrdinalIgnoreCase))?.Pinned ?? false;

            _entries.RemoveAll(e => string.Equals(e.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
            _entries.Insert(0, new RecentFileEntry { FilePath = filePath, Name = name, LastOpenedUtc = DateTime.UtcNow, Pinned = wasPinned });
            TrimToCapPreservingPinned();

            await WriteAllAsync(_entries);
        }
        finally
        {
            _lock.Release();
        }
    }

    public IReadOnlyList<RecentFileEntry> GetRecentFiles(int maxCount = 20) =>
        _entries.OrderByDescending(e => e.Pinned).ThenByDescending(e => e.LastOpenedUtc).Take(maxCount).ToList();

    public async Task RemoveAsync(string filePath)
    {
        await _lock.WaitAsync();
        try
        {
            _entries.RemoveAll(e => string.Equals(e.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
            await WriteAllAsync(_entries);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SetPinnedAsync(string filePath, bool pinned)
    {
        await _lock.WaitAsync();
        try
        {
            var entry = _entries.FirstOrDefault(e => string.Equals(e.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
            if (entry is null) return;

            entry.Pinned = pinned;
            await WriteAllAsync(_entries);
        }
        finally
        {
            _lock.Release();
        }
    }

    private void TrimToCapPreservingPinned()
    {
        if (_entries.Count <= MaxStored) return;

        // Pinned entries never get evicted by the storage cap just for being old --
        // otherwise pinning a file wouldn't actually keep it around, which defeats the
        // point of pinning. Unpinned entries fill whatever room is left, most-recent
        // first (list is already maintained newest-first by TrackAsync's Insert(0)).
        var pinned = _entries.Where(e => e.Pinned).ToList();
        var unpinned = _entries.Where(e => !e.Pinned).Take(Math.Max(0, MaxStored - pinned.Count));
        _entries = pinned.Concat(unpinned).ToList();
    }

    private List<RecentFileEntry> LoadFromDisk()
    {
        if (!File.Exists(_filePath)) return new List<RecentFileEntry>();

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<List<RecentFileEntry>>(json) ?? new List<RecentFileEntry>();
        }
        catch
        {
            // Corrupt/partially-written file must never stop the app from starting.
            return new List<RecentFileEntry>();
        }
    }

    private async Task WriteAllAsync(List<RecentFileEntry> entries)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(entries, options);
        var tempPath = _filePath + ".tmp";
        await File.WriteAllTextAsync(tempPath, json);
        File.Copy(tempPath, _filePath, overwrite: true);
        File.Delete(tempPath);
    }
}
