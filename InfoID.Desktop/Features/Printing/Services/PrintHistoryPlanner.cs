using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using InfoID.Desktop.Features.Printing.Models;

namespace InfoID.Desktop.Features.Printing.Services;

/// <summary>
/// Pure helpers behind Print history's per-design settings: finding the Person ID and
/// name columns in a data source, working out each card's expiry date, and building the
/// key a design's settings are remembered under. No UI or database access, so it is
/// unit-testable on its own.
/// </summary>
public static class PrintHistoryPlanner
{
    /// <summary>Stored as the Person ID column when the user explicitly chose
    /// "(none)", so auto-detection doesn't pick a column again next time.</summary>
    public const string NoColumn = "(none)";

    // Most specific first: "EmployeeNo" must win over a generic "No" or "Code".
    private static readonly string[] PersonIdCandidates =
    {
        "visitorid", "visitorcode", "visitorno",
        "employeeid", "employeeno", "employeenumber", "employeecode", "empid", "empno", "empcode",
        "staffid", "staffno", "staffcode", "studentid", "studentno", "rollno", "rollnumber",
        "admissionno", "admissionnumber", "registrationno", "regno", "registerno", "enrollmentno",
        "memberid", "memberno", "membershipno", "personid", "personno", "cardholderid",
        "badgeno", "badgeid", "idno", "idnumber", "id", "no", "code", "number",
    };

    private static readonly string[] NameCandidates =
    {
        "fullname", "name", "employeename", "empname", "staffname", "studentname", "membername",
        "cardholdername", "personname", "displayname",
    };

    private static readonly string[] ExpiryCandidates =
    {
        "expirydate", "expiry", "expirationdate", "expiresOn", "validtill", "validupto", "validuntil",
        "validto", "validthru", "dateofexpiry", "enddate",
    };

    private static readonly string[] DateFormats =
    {
        "yyyy-MM-dd", "yyyy/MM/dd", "dd/MM/yyyy", "dd-MM-yyyy", "dd.MM.yyyy", "d/M/yyyy", "d-M-yyyy",
        "MM/dd/yyyy", "M/d/yyyy", "dd MMM yyyy", "d MMM yyyy", "dd-MMM-yyyy", "MMM yyyy", "MM/yyyy", "MM-yyyy",
        "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm:ss",
    };

    public static string? DetectPersonIdColumn(IEnumerable<string> columns) => Detect(columns, PersonIdCandidates);

    public static string? DetectNameColumn(IEnumerable<string> columns) => Detect(columns, NameCandidates);

    public static string? DetectExpiryColumn(IEnumerable<string> columns) => Detect(columns, ExpiryCandidates);

    private static string? Detect(IEnumerable<string> columns, string[] candidates)
    {
        var list = columns.Where(c => !string.IsNullOrWhiteSpace(c)).ToList();
        foreach (var candidate in candidates)
        {
            var match = list.FirstOrDefault(c => Normalize(c) == candidate.ToLowerInvariant());
            if (match is not null) return match;
        }
        return null;
    }

    private static string Normalize(string column) =>
        new string(column.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    /// <summary>The settings key for a design: its saved template id, else its file
    /// path, else its name (an unsaved design).</summary>
    public static string BuildDesignKey(long? templateId, string? sourceFilePath, string? name)
    {
        if (templateId is { } id) return "template:" + id.ToString(CultureInfo.InvariantCulture);
        if (!string.IsNullOrWhiteSpace(sourceFilePath)) return "file:" + sourceFilePath.Trim().ToLowerInvariant();
        return "name:" + (string.IsNullOrWhiteSpace(name) ? "untitled" : name.Trim().ToLowerInvariant());
    }

    /// <summary>Expiry for one printed card, or null for no expiry (also when the
    /// expiry column is empty or not a recognizable date).</summary>
    public static DateTime? ComputeExpiry(PrintHistorySettings settings, IReadOnlyDictionary<string, string> fields, DateTime today)
    {
        today = today.Date;
        return settings.Validity switch
        {
            CardValidityMode.OneYear => today.AddYears(1).AddDays(-1),
            CardValidityMode.TwoYears => today.AddYears(2).AddDays(-1),
            CardValidityMode.ThreeYears => today.AddYears(3).AddDays(-1),
            CardValidityMode.UntilDate => settings.ValidUntil?.Date,
            CardValidityMode.FromColumn => !string.IsNullOrWhiteSpace(settings.ExpiryColumn) &&
                                           fields.TryGetValue(settings.ExpiryColumn, out var raw)
                ? ParseDate(raw)
                : null,
            _ => null,
        };
    }

    /// <summary>Parses the common date spellings found in spreadsheets and databases.
    /// "MM/yyyy" style values mean the end of that month.</summary>
    public static DateTime? ParseDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var text = raw.Trim();

        foreach (var format in DateFormats)
        {
            if (DateTime.TryParseExact(text, format, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var exact))
            {
                return format is "MMM yyyy" or "MM/yyyy" or "MM-yyyy"
                    ? new DateTime(exact.Year, exact.Month, DateTime.DaysInMonth(exact.Year, exact.Month))
                    : exact.Date;
            }
        }

        if (DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out var local)) return local.Date;
        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var invariant)) return invariant.Date;

        // Excel serial date (e.g. 46022 = 31 Dec 2025) from spreadsheet imports.
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial) && serial is > 20000 and < 80000)
            return DateTime.FromOADate(serial).Date;

        return null;
    }

    public static string ValidityLabel(CardValidityMode mode) => mode switch
    {
        CardValidityMode.NoExpiry => "No expiry",
        CardValidityMode.OneYear => "1 year",
        CardValidityMode.TwoYears => "2 years",
        CardValidityMode.ThreeYears => "3 years",
        CardValidityMode.UntilDate => "Until a date",
        CardValidityMode.FromColumn => "From a data column",
        _ => mode.ToString(),
    };
}
