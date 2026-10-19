using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;

namespace InfoID.Desktop.Features.CardDesigner.Services;

/// <summary>Result of checking which records have a matching photo.</summary>
public sealed record PhotoMatchReport(
    int TotalRecords,
    int MatchedRecords,
    IReadOnlyList<string> MissingKeys)
{
    public int MissingRecords => TotalRecords - MatchedRecords;
}

/// <summary>
/// Links employee photos stored as files in a folder to database records by file
/// name, e.g. the record with EmployeeID 1001 uses "1001.jpg". Photos are read in
/// place (never copied into the assets folder), so a folder of thousands of photos
/// costs no extra disk space and HR can update photos without re-importing.
/// </summary>
public interface IPhotoFolderService
{
    /// <summary>Scans the folder once (on a background thread) and keeps its file
    /// list up to date while InfoID runs. Call when the folder is chosen or a design
    /// is opened; lookups are instant afterwards.</summary>
    Task PrepareFolderAsync(string folderPath, CancellationToken cancellationToken = default);

    /// <summary>Rescans the folder (the "Refresh" button).</summary>
    Task RefreshFolderAsync(string folderPath, CancellationToken cancellationToken = default);

    /// <summary>Builds the file-name key for one record from a pattern such as
    /// "{EmployeeID}" or "EMP-{EmployeeID}", using the record's field values.
    /// Returns null if a field in the pattern is missing or empty.</summary>
    string? BuildKey(string pattern, IReadOnlyDictionary<string, string> record);

    /// <summary>Full path of the photo for this key, or null if there is none.
    /// The folder must have been prepared first.</summary>
    string? FindPhoto(string folderPath, string key);

    /// <summary>Loads a photo scaled to the width it will be drawn at. Recently used
    /// photos are cached, so moving between records stays fast.</summary>
    Bitmap? LoadPhoto(string photoPath, int decodeWidth);

    /// <summary>Counts which records have a matching photo, for the
    /// "112 of 120 employees have a photo" check before printing.</summary>
    PhotoMatchReport CheckRecords(
        string folderPath,
        string pattern,
        IEnumerable<IReadOnlyDictionary<string, string>> records);

    /// <summary>Picks the column most likely to identify employees
    /// (EmployeeID, Employee ID, EmpID, ... then ID), for the default pattern.</summary>
    string? SuggestKeyColumn(IEnumerable<string> columnNames);
}

public sealed class PhotoFolderService : IPhotoFolderService, IDisposable
{
    private static readonly InfoID.Shared.Logging.AppLogger Log = InfoID.Shared.Logging.AppLog.For<PhotoFolderService>();

    /// <summary>One shared instance for the whole app, so every open design tab uses
    /// the same folder scans and photo cache. Can be moved to DI registration later
    /// without changing any callers' behavior.</summary>
    public static PhotoFolderService Shared { get; } = new();

    /// <summary>In priority order: if both 1001.jpg and 1001.png exist, .jpg wins.</summary>
    private static readonly string[] SupportedExtensions =
        { ".jpg", ".jpeg", ".png", ".webp", ".bmp" };

    private static readonly string[] PreferredKeyColumns =
        { "EmployeeID", "Employee ID", "Employee_ID", "EmpID", "Emp ID", "Emp_ID", "EmployeeNo", "Employee No", "StaffID", "Staff ID", "ID" };

    private static readonly Regex PlaceholderRegex = new(@"\{([^{}]+)\}", RegexOptions.Compiled);

    private const int MaxCachedBitmaps = 200;

    private readonly ConcurrentDictionary<string, FolderIndex> _folders =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly object _cacheLock = new();
    private readonly Dictionary<string, LinkedListNode<CachedBitmap>> _bitmapCache = new();
    private readonly LinkedList<CachedBitmap> _bitmapOrder = new();

    private sealed record CachedBitmap(string CacheKey, Bitmap? Bitmap);

    // ------------------------------------------------------------ folders ----

    public async Task PrepareFolderAsync(string folderPath, CancellationToken cancellationToken = default)
    {
        var fullPath = NormalizeFolder(folderPath);
        if (fullPath is null || _folders.ContainsKey(fullPath))
            return;

        var index = new FolderIndex(fullPath);
        await Task.Run(index.Rebuild, cancellationToken);

        if (!_folders.TryAdd(fullPath, index))
            index.Dispose(); // another caller prepared it at the same time
    }

    public async Task RefreshFolderAsync(string folderPath, CancellationToken cancellationToken = default)
    {
        var fullPath = NormalizeFolder(folderPath);
        if (fullPath is null)
            return;

        if (_folders.TryGetValue(fullPath, out var index))
            await Task.Run(index.Rebuild, cancellationToken);
        else
            await PrepareFolderAsync(fullPath, cancellationToken);

        ClearBitmapCache();
    }

    public string? FindPhoto(string folderPath, string key)
    {
        var fullPath = NormalizeFolder(folderPath);
        if (fullPath is null || string.IsNullOrWhiteSpace(key))
            return null;

        return _folders.TryGetValue(fullPath, out var index)
            ? index.Find(key.Trim())
            : null;
    }

    // --------------------------------------------------------------- keys ----

    public string? BuildKey(string pattern, IReadOnlyDictionary<string, string> record)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return null;

        var missingField = false;

        var key = PlaceholderRegex.Replace(pattern, match =>
        {
            var fieldName = match.Groups[1].Value.Trim();
            var value = GetFieldValue(record, fieldName);

            if (string.IsNullOrWhiteSpace(value))
            {
                missingField = true;
                return string.Empty;
            }

            return NormalizeValue(value);
        });

        return missingField || string.IsNullOrWhiteSpace(key) ? null : key.Trim();
    }

    public string? SuggestKeyColumn(IEnumerable<string> columnNames)
    {
        var columns = columnNames.ToList();

        foreach (var preferred in PreferredKeyColumns)
        {
            var match = columns.FirstOrDefault(c =>
                string.Equals(Compact(c), Compact(preferred), StringComparison.OrdinalIgnoreCase));

            if (match is not null)
                return match;
        }

        return null;

        static string Compact(string s) => s.Replace(" ", "").Replace("_", "");
    }

    /// <summary>Field names are matched case-insensitively and ignoring spaces, so a
    /// pattern "{EmployeeID}" also works for a column called "Employee ID".</summary>
    private static string? GetFieldValue(IReadOnlyDictionary<string, string> record, string fieldName)
    {
        if (record.TryGetValue(fieldName, out var exact))
            return exact;

        var compactName = fieldName.Replace(" ", "");

        foreach (var (name, value) in record)
        {
            if (string.Equals(name.Replace(" ", ""), compactName, StringComparison.OrdinalIgnoreCase))
                return value;
        }

        return null;
    }

    /// <summary>Excel stores numbers as decimals, so an ID can arrive as "1001.0"
    /// or "1001.00"; the photo is still "1001.jpg".</summary>
    private static string NormalizeValue(string value)
    {
        var trimmed = value.Trim();

        if (decimal.TryParse(trimmed, System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out var number) &&
            number == decimal.Truncate(number) &&
            trimmed.Contains('.'))
        {
            return decimal.Truncate(number).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return trimmed;
    }

    // ------------------------------------------------------------ bitmaps ----

    public Bitmap? LoadPhoto(string photoPath, int decodeWidth)
    {
        if (string.IsNullOrWhiteSpace(photoPath))
            return null;

        decodeWidth = Math.Clamp(decodeWidth, 16, 2048);

        DateTime lastWrite;
        try
        {
            lastWrite = File.GetLastWriteTimeUtc(photoPath);
        }
        catch (Exception)
        {
            return null;
        }

        // Including the file's last-write time means a photo HR replaces is reloaded.
        var cacheKey = $"{photoPath}|{decodeWidth}|{lastWrite.Ticks}";

        lock (_cacheLock)
        {
            if (_bitmapCache.TryGetValue(cacheKey, out var node))
            {
                _bitmapOrder.Remove(node);
                _bitmapOrder.AddFirst(node);
                return node.Value.Bitmap;
            }
        }

        Bitmap? bitmap = null;
        try
        {
            using var stream = File.OpenRead(photoPath);
            bitmap = Bitmap.DecodeToWidth(stream, decodeWidth);
        }
        catch (Exception ex)
        {
            // A corrupt or locked file shows as "no photo" instead of crashing.
            Log.Warn($"Photo couldn't be loaded ({photoPath})", ex);
        }

        lock (_cacheLock)
        {
            var node = new LinkedListNode<CachedBitmap>(new CachedBitmap(cacheKey, bitmap));
            _bitmapOrder.AddFirst(node);
            _bitmapCache[cacheKey] = node;

            // Oldest entries are dropped from the cache but deliberately not disposed:
            // the canvas or a preview may still be drawing them. The GC frees them.
            while (_bitmapOrder.Count > MaxCachedBitmaps)
            {
                var oldest = _bitmapOrder.Last!;
                _bitmapOrder.RemoveLast();
                _bitmapCache.Remove(oldest.Value.CacheKey);
            }
        }

        return bitmap;
    }

    private void ClearBitmapCache()
    {
        lock (_cacheLock)
        {
            _bitmapCache.Clear();
            _bitmapOrder.Clear();
        }
    }

    // ------------------------------------------------------------- report ----

    public PhotoMatchReport CheckRecords(
        string folderPath,
        string pattern,
        IEnumerable<IReadOnlyDictionary<string, string>> records)
    {
        var total = 0;
        var matched = 0;
        var missing = new List<string>();

        foreach (var record in records)
        {
            total++;

            var key = BuildKey(pattern, record);
            if (key is not null && FindPhoto(folderPath, key) is not null)
            {
                matched++;
            }
            else
            {
                missing.Add(key ?? "(no value)");
            }
        }

        return new PhotoMatchReport(total, matched, missing);
    }

    // ------------------------------------------------------------ helpers ----

    private static string? NormalizeFolder(string? folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            return null;

        try
        {
            var fullPath = Path.GetFullPath(folderPath.Trim());
            return Directory.Exists(fullPath) ? fullPath : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Dispose()
    {
        foreach (var index in _folders.Values)
            index.Dispose();

        _folders.Clear();
        ClearBitmapCache();
    }

    // -------------------------------------------------------- folder index ----

    /// <summary>File-name lookup for one folder, kept current by a FileSystemWatcher
    /// so photos added, renamed or deleted while InfoID is open are picked up.</summary>
    private sealed class FolderIndex : IDisposable
    {
        private readonly string _folderPath;
        private readonly object _lock = new();
        private readonly FileSystemWatcher? _watcher;

        // "1001" -> "C:\Photos\1001.jpg"
        private Dictionary<string, string> _byName = new(StringComparer.OrdinalIgnoreCase);

        // Numeric names without leading zeros: "01001.jpg" still matches ID 1001.
        private Dictionary<string, string> _byNumber = new(StringComparer.OrdinalIgnoreCase);

        public FolderIndex(string folderPath)
        {
            _folderPath = folderPath;

            try
            {
                _watcher = new FileSystemWatcher(folderPath)
                {
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite
                };

                _watcher.Created += (_, _) => Rebuild();
                _watcher.Deleted += (_, _) => Rebuild();
                _watcher.Renamed += (_, _) => Rebuild();
                _watcher.Error += (_, _) => Rebuild(); // e.g. too many changes at once
                _watcher.EnableRaisingEvents = true;
            }
            catch (Exception ex)
            {
                // Watching is a convenience; the Refresh button still works without it.
                Log.Warn("Photo folder watch error", ex);
                _watcher = null;
            }
        }

        /// <summary>Full rescan. Cheap even for thousands of files: it only reads
        /// names, never file contents.</summary>
        public void Rebuild()
        {
            var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var byNumber = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var files = Directory
                    .EnumerateFiles(_folderPath)
                    .Select(path => (Path: path, Rank: Array.IndexOf(
                        SupportedExtensions,
                        Path.GetExtension(path).ToLowerInvariant())))
                    .Where(f => f.Rank >= 0)
                    .OrderBy(f => f.Rank); // preferred extension first

                foreach (var (path, _) in files)
                {
                    var name = Path.GetFileNameWithoutExtension(path).Trim();
                    if (name.Length == 0)
                        continue;

                    byName.TryAdd(name, path);

                    var numberKey = ToNumberKey(name);
                    if (numberKey is not null)
                        byNumber.TryAdd(numberKey, path);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Photo folder scan error", ex);
            }

            lock (_lock)
            {
                _byName = byName;
                _byNumber = byNumber;
            }
        }

        public string? Find(string key)
        {
            lock (_lock)
            {
                if (_byName.TryGetValue(key, out var path))
                    return path;

                var numberKey = ToNumberKey(key);
                return numberKey is not null && _byNumber.TryGetValue(numberKey, out path)
                    ? path
                    : null;
            }
        }

        private static string? ToNumberKey(string value)
        {
            if (value.Length == 0 || !value.All(char.IsDigit))
                return null;

            var trimmed = value.TrimStart('0');
            return trimmed.Length == 0 ? "0" : trimmed;
        }

        public void Dispose() => _watcher?.Dispose();
    }
}
