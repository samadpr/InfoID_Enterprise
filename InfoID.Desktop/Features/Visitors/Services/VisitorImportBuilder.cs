using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using InfoID.Desktop.Features.Visitors.ViewModels;

namespace InfoID.Desktop.Features.Visitors.Services;

/// <summary>
/// Builds the import review rows from a file and the chosen column matches (blank rows are
/// skipped): core fields
/// from their columns, every other column kept as an extra field, the photo looked up
/// (next to the file or in the chosen photos folder), and repeated rows flagged.
/// </summary>
public static class VisitorImportBuilder
{
    private static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".webp", ".gif" };

    public static List<VisitorImportRowViewModel> BuildRows(
        VisitorFileData file,
        IReadOnlyDictionary<VisitorImportField, string> mapping,
        string? photosFolder,
        DateOnly today)
    {
        var mappedColumns = new HashSet<string>(mapping.Values, StringComparer.OrdinalIgnoreCase);
        var extraColumns = file.Columns.Where(c => !mappedColumns.Contains(c)).ToList();
        var rows = new List<VisitorImportRowViewModel>();
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);

        var rowNumber = 1; // header
        foreach (DataRow data in file.Table.Rows)
        {
            rowNumber++;
            // Completely empty rows (blank lines in the file) are skipped, but still counted
            // so "Row 12" matches the file.
            if (data.ItemArray.All(v => v is null or DBNull || string.IsNullOrWhiteSpace(v.ToString()))) continue;
            string Get(VisitorImportField field) =>
                mapping.TryGetValue(field, out var column) && file.Table.Columns.Contains(column)
                    ? data[column]?.ToString()?.Trim() ?? string.Empty
                    : string.Empty;

            var row = new VisitorImportRowViewModel(rowNumber, today);
            row.BeginInit();
            row.FullName = Get(VisitorImportField.FullName);
            row.Phone = Get(VisitorImportField.Phone);
            row.Email = Get(VisitorImportField.Email);
            row.Company = Get(VisitorImportField.Company);
            row.PersonToMeet = Get(VisitorImportField.PersonToMeet);
            row.Purpose = Get(VisitorImportField.Purpose);
            row.VisitDateText = Get(VisitorImportField.VisitDate);
            row.ResponseKey = NullIfEmpty(Get(VisitorImportField.ResponseKey));
            row.PhotoText = Get(VisitorImportField.Photo);
            (row.PhotoFile, row.PhotoProblem) = ResolvePhoto(row.PhotoText, file.Folder, photosFolder);

            foreach (var column in extraColumns)
            {
                var value = data[column]?.ToString()?.Trim();
                if (!string.IsNullOrEmpty(value)) row.ExtraFields[column] = value;
            }
            row.EndInit();

            row.MarkDuplicate(alreadyImported: false, duplicateInFile: !seenKeys.Add(row.SourceKey));
            rows.Add(row);
        }
        return rows;
    }

    /// <summary>
    /// Finds the photo for a row: an existing full path; a file name next to the import file
    /// or in the photos folder (with or without its extension); otherwise the reason it can't
    /// be used. Web links (e.g. Google Drive) aren't downloaded in this version.
    /// </summary>
    public static (string? File, string? Problem) ResolvePhoto(string? text, string fileFolder, string? photosFolder)
    {
        if (string.IsNullOrWhiteSpace(text)) return (null, null);
        var value = text.Trim().Trim('"');

        if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return (null, "Photo links (e.g. Google Drive) aren't downloaded yet: add the photo after importing, or use a photos folder.");
        }

        foreach (var candidate in Candidates(value, fileFolder, photosFolder))
        {
            if (!File.Exists(candidate)) continue;
            var problem = VisitorPhotoStore.CheckImage(candidate);
            return problem is null ? (candidate, null) : (null, problem);
        }
        return (null, $"Photo \"{value}\" not found next to the file{(string.IsNullOrWhiteSpace(photosFolder) ? "" : " or in the photos folder")}.");
    }

    private static IEnumerable<string> Candidates(string value, string fileFolder, string? photosFolder)
    {
        string? Safe(Func<string?> build)
        {
            try { return build(); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
        }

        var names = new List<string> { value };
        if (!Path.HasExtension(value)) names.AddRange(ImageExtensions.Select(e => value + e));

        foreach (var name in names)
        {
            if (Safe(() => Path.IsPathRooted(name) ? name : null) is { } rooted) yield return rooted;
            if (!string.IsNullOrWhiteSpace(fileFolder) && Safe(() => Path.Combine(fileFolder, name)) is { } nextToFile) yield return nextToFile;
            if (!string.IsNullOrWhiteSpace(photosFolder))
            {
                if (Safe(() => Path.Combine(photosFolder, name)) is { } inFolder) yield return inFolder;
                if (Safe(() => Path.Combine(photosFolder, Path.GetFileName(name))) is { } byName) yield return byName;
            }
        }
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
