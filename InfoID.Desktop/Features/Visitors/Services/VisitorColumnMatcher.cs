using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace InfoID.Desktop.Features.Visitors.Services;

/// <summary>The visitor fields a file column (or form question) can be matched to.</summary>
public enum VisitorImportField
{
    FullName,
    Phone,
    Email,
    Company,
    PersonToMeet,
    Purpose,
    VisitDate,
    Photo,
    /// <summary>A response id or submission timestamp: identifies the registration so
    /// importing the same file again doesn't create duplicates.</summary>
    ResponseKey,
}

/// <summary>
/// Matches file columns / Google Form questions to visitor fields by name, so a typical
/// form export ("Full name", "Mobile number", "Whom do you want to meet?", "Upload your
/// photo") maps without any setup. The user can change every match in the import review.
/// </summary>
public static class VisitorColumnMatcher
{
    private static readonly (VisitorImportField Field, string[] Names)[] Aliases =
    {
        (VisitorImportField.ResponseKey, new[] { "sourceid", "responseid", "submissionid", "timestamp", "submittedat", "submissiontime", "submissiondate" }),
        (VisitorImportField.FullName, new[] { "fullname", "name", "visitorname", "visitorsname", "yourname", "yourfullname", "nameofvisitor", "nameofthevisitor", "guestname" }),
        (VisitorImportField.Email, new[] { "email", "emailaddress", "emailid", "mail", "youremail", "youremailaddress" }),
        (VisitorImportField.Phone, new[] { "phone", "phonenumber", "mobile", "mobilenumber", "mobileno", "phoneno", "contactnumber", "contactno", "whatsapp", "whatsappnumber", "telephone" }),
        (VisitorImportField.Company, new[] { "company", "companyname", "organization", "organisation", "organizationname", "organisationname", "firm" }),
        (VisitorImportField.Purpose, new[] { "purpose", "purposeofvisit", "reason", "reasonforvisit", "visitpurpose" }),
        (VisitorImportField.PersonToMeet, new[] { "persontomeet", "hostname", "host", "whomtomeet", "whomdoyouwanttomeet", "whoareyoumeeting", "meetingwith", "tomeet", "contactperson", "visiting", "meeting" }),
        (VisitorImportField.VisitDate, new[] { "visitdate", "dateofvisit", "visitingdate", "date", "expectedvisitdate", "dateofarrival" }),
        (VisitorImportField.Photo, new[] { "photo", "picture", "image", "photograph", "uploadphoto", "uploadyourphoto", "yourphoto", "photourl", "photofile", "selfie" }),
    };

    /// <summary>Too general to match inside a longer question ("Purpose of meeting").</summary>
    private static readonly HashSet<string> ExactOnly = new(StringComparer.Ordinal) { "meeting", "visiting", "tomeet", "reason", "image", "submissiondate" };

    /// <summary>Best column per field (exact name first, then "contains"), each column used once.</summary>
    public static Dictionary<VisitorImportField, string> Match(IReadOnlyList<string> columns)
    {
        var result = new Dictionary<VisitorImportField, string>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalized = columns.Select(c => (Column: c, Key: Normalize(c))).ToList();

        // Pass 1: exact names (in the order of each field's aliases).
        foreach (var (field, names) in Aliases)
        {
            foreach (var name in names)
            {
                var hit = normalized.FirstOrDefault(c => !used.Contains(c.Column) && c.Key == name);
                if (hit.Column is null) continue;
                result[field] = hit.Column;
                used.Add(hit.Column);
                break;
            }
        }

        // Pass 2: long form questions that contain a known name ("Upload your photo (passport size)").
        foreach (var (field, names) in Aliases)
        {
            if (result.ContainsKey(field)) continue;
            foreach (var name in names.Where(n => n.Length >= 5 && !ExactOnly.Contains(n)))
            {
                var hit = normalized.FirstOrDefault(c => !used.Contains(c.Column) && c.Key.Contains(name, StringComparison.Ordinal));
                if (hit.Column is null) continue;
                result[field] = hit.Column;
                used.Add(hit.Column);
                break;
            }
        }
        return result;
    }

    /// <summary>"Whom do you want to meet?" -> "whomdoyouwanttomeet".</summary>
    public static string Normalize(string? text) =>
        new string((text ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    /// <summary>
    /// The identity of one registration, so the same row is never imported twice:
    /// the response id / timestamp column when there is one (plus the email or phone, since
    /// two people can submit in the same second), otherwise a hash of the row's main values.
    /// </summary>
    public static string BuildSourceKey(string? responseKey, string? name, string? phone, string? email, string? visitDate)
    {
        var contact = Normalize(email).Length > 0 ? Normalize(email) : Normalize(phone);
        if (!string.IsNullOrWhiteSpace(responseKey))
            return "src:" + Normalize(responseKey) + "|" + (contact.Length > 0 ? contact : Normalize(name));

        var basis = string.Join("|", Normalize(name), Normalize(phone), Normalize(email), Normalize(visitDate));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(basis));
        return "row:" + Convert.ToHexStringLower(hash.AsSpan(0, 16));
    }

    private static readonly string[] DateFormats =
    {
        "yyyy-MM-dd", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ssK", "yyyy/MM/dd",
        "d/M/yyyy", "d/M/yyyy H:mm:ss", "d-M-yyyy", "d.M.yyyy", "d MMM yyyy", "d MMMM yyyy", "d-MMM-yyyy",
        "MMM d, yyyy", "MMMM d, yyyy", "M/d/yyyy", "M/d/yyyy H:mm:ss",
    };

    /// <summary>
    /// Reads a visit date. ISO dates (2026-10-15, what InfoID's JSON format uses) first, then
    /// day-first and the PC's own date format, then month-first, then an Excel serial number.
    /// Returns false for text that isn't a date. "3/4/2026" is read day-first (3 April) unless
    /// the PC's format is month-first -- the review shows the date it understood.
    /// </summary>
    public static bool TryParseDate(string? text, out DateOnly date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var value = text.Trim();

        if (DateTime.TryParseExact(value, DateFormats.Take(5).ToArray(), CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var iso))
        {
            date = DateOnly.FromDateTime(iso);
            return true;
        }

        var monthFirst = CultureInfo.CurrentCulture.DateTimeFormat.ShortDatePattern.TrimStart().StartsWith("M", StringComparison.OrdinalIgnoreCase);
        if (monthFirst && DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out var local))
        {
            date = DateOnly.FromDateTime(local);
            return true;
        }
        if (DateTime.TryParseExact(value, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed) ||
            DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out parsed))
        {
            date = DateOnly.FromDateTime(parsed);
            return true;
        }
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial) && serial is > 20000 and < 80000)
        {
            date = DateOnly.FromDateTime(DateTime.FromOADate(serial));
            return true;
        }
        return false;
    }
}
