using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using InfoID.Application.Dtos.VisitorModule;

namespace InfoID.Desktop.Features.Visitors.Services;

/// <summary>
/// Turns visitors into the row table the Card Designer and Print dialog work with, so a
/// visitor badge is an ordinary design bound to these field names. One row per visitor;
/// bulk printing prints one badge per row exactly like any other data source.
/// "VisitorId" is the Person ID Print history records each badge under (that is how the
/// Visitors list knows a badge was printed); "Photo" is the full path of the stored photo.
/// </summary>
public static class VisitorBadgeData
{
    public const string SourceName = "Visitors";
    public const string TableName = "Visitors";

    public const string VisitorId = "VisitorId";
    public const string FullName = "FullName";
    public const string Company = "Company";
    public const string PersonToMeet = "PersonToMeet";
    public const string Purpose = "Purpose";
    public const string VisitDate = "VisitDate";
    public const string VisitDay = "VisitDay";
    public const string Phone = "Phone";
    public const string Email = "Email";
    public const string Photo = "Photo";

    /// <summary>The fixed fields, in the order the designer lists them.</summary>
    public static readonly IReadOnlyList<string> CoreColumns = new[]
    {
        VisitorId, FullName, Company, PersonToMeet, Purpose, VisitDate, VisitDay, Phone, Email, Photo,
    };

    public static DataTable BuildTable(IEnumerable<VisitorDto> visitors, VisitorPhotoStore photos)
    {
        var list = visitors.ToList();
        var table = new DataTable(TableName);
        foreach (var column in CoreColumns) table.Columns.Add(column, typeof(string));

        // Extra form questions / file columns become extra fields, named so they're valid
        // and never collide with the fixed ones.
        var extraColumns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in list.SelectMany(v => v.ExtraFields.Keys))
        {
            if (extraColumns.ContainsKey(key)) continue;
            var name = ColumnNameFor(key);
            var unique = name;
            for (var n = 2; table.Columns.Contains(unique); n++) unique = name + n.ToString(CultureInfo.InvariantCulture);
            table.Columns.Add(unique, typeof(string));
            extraColumns[key] = unique;
        }

        foreach (var v in list)
        {
            var row = table.NewRow();
            row[VisitorId] = v.VisitorCode;
            row[FullName] = v.FullName;
            row[Company] = v.Company ?? string.Empty;
            row[PersonToMeet] = v.PersonToMeet ?? string.Empty;
            row[Purpose] = v.Purpose ?? string.Empty;
            row[VisitDate] = v.VisitDate?.ToString("dd MMM yyyy", CultureInfo.CurrentCulture) ?? string.Empty;
            row[VisitDay] = v.VisitDate?.ToString("ddd", CultureInfo.CurrentCulture).ToUpperInvariant() ?? string.Empty;
            row[Phone] = v.Phone ?? string.Empty;
            row[Email] = v.Email ?? string.Empty;
            row[Photo] = photos.GetFullPath(v.PhotoPath) ?? string.Empty;
            foreach (var (key, column) in extraColumns)
                row[column] = v.ExtraFields.TryGetValue(key, out var value) ? value : string.Empty;
            table.Rows.Add(row);
        }

        table.AcceptChanges();
        return table;
    }

    /// <summary>"Vehicle number?" -> "VehicleNumber".</summary>
    internal static string ColumnNameFor(string question)
    {
        var words = question.Split(new[] { ' ', '_', '-', '.', '/', '\\', '(', ')', '?', ':', ',' }, StringSplitOptions.RemoveEmptyEntries);
        var name = string.Concat(words.Select(w =>
        {
            var clean = new string(w.Where(char.IsLetterOrDigit).ToArray());
            return clean.Length == 0 ? string.Empty : char.ToUpperInvariant(clean[0]) + clean[1..];
        }));
        if (name.Length == 0) name = "Field";
        if (char.IsDigit(name[0])) name = "Field" + name;
        return name.Length > 60 ? name[..60] : name;
    }
}
