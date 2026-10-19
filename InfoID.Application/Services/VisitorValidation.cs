using System.Text.RegularExpressions;
using InfoID.Application.Dtos.VisitorModule;

namespace InfoID.Application.Services;

/// <summary>
/// The one set of rules for a visitor registration, used by the import review, the
/// Add/Edit dialog and the service itself (so nothing invalid reaches the database).
///
/// Errors (the row can't be saved): name missing; neither phone nor email; email not an
/// email address; a value longer than its column.
/// Warnings (can be saved): no visit date (today's date is used), visit date in the past,
/// no photo.
/// </summary>
public static partial class VisitorValidation
{
    public const int MaxName = 200;
    public const int MaxPhone = 50;
    public const int MaxEmail = 200;
    public const int MaxCompany = 200;
    public const int MaxPersonToMeet = 200;
    public const int MaxPurpose = 500;

    public const string FieldName = "Full name";
    public const string FieldPhone = "Phone";
    public const string FieldEmail = "Email";
    public const string FieldCompany = "Company";
    public const string FieldPersonToMeet = "Person to meet";
    public const string FieldPurpose = "Purpose";
    public const string FieldVisitDate = "Visit date";
    public const string FieldPhoto = "Photo";

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();

    /// <param name="today">Local date, for the "visit date in the past" warning.</param>
    public static List<VisitorIssue> Validate(VisitorInput input, DateOnly today)
    {
        var issues = new List<VisitorIssue>();

        if (string.IsNullOrWhiteSpace(input.FullName))
            issues.Add(Error(FieldName, "Name is required."));
        if (string.IsNullOrWhiteSpace(input.Phone) && string.IsNullOrWhiteSpace(input.Email))
            issues.Add(Error(FieldPhone, "Enter a phone number or an email address."));
        if (!string.IsNullOrWhiteSpace(input.Email) && !EmailPattern().IsMatch(input.Email.Trim()))
            issues.Add(Error(FieldEmail, "This isn't a valid email address."));

        TooLong(issues, FieldName, input.FullName, MaxName);
        TooLong(issues, FieldPhone, input.Phone, MaxPhone);
        TooLong(issues, FieldEmail, input.Email, MaxEmail);
        TooLong(issues, FieldCompany, input.Company, MaxCompany);
        TooLong(issues, FieldPersonToMeet, input.PersonToMeet, MaxPersonToMeet);
        TooLong(issues, FieldPurpose, input.Purpose, MaxPurpose);

        if (input.VisitDate is null)
            issues.Add(Warning(FieldVisitDate, "No visit date: today's date will be used."));
        else if (input.VisitDate < today)
            issues.Add(Warning(FieldVisitDate, "The visit date is in the past."));

        if (string.IsNullOrWhiteSpace(input.PhotoPath))
            issues.Add(Warning(FieldPhoto, "No photo: the badge prints without one."));

        return issues;
    }

    public static bool HasErrors(IEnumerable<VisitorIssue> issues) =>
        issues.Any(i => i.Severity == VisitorIssueSeverity.Error);

    /// <summary>Trims every text field and drops empty ones, so stored data is clean.</summary>
    public static void Normalize(VisitorInput input)
    {
        input.FullName = CollapseSpaces(input.FullName);
        input.Phone = Clean(input.Phone);
        input.Email = Clean(input.Email)?.ToLowerInvariant();
        input.Company = Clean(input.Company);
        input.PersonToMeet = Clean(input.PersonToMeet);
        input.Purpose = Clean(input.Purpose);
        input.PhotoPath = Clean(input.PhotoPath);

        var extras = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in input.ExtraFields)
        {
            var k = Clean(key);
            var v = Clean(value);
            if (k is not null && v is not null) extras[k] = v;
        }
        input.ExtraFields = extras;
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string CollapseSpaces(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : Regex.Replace(value.Trim(), @"\s+", " ");

    private static void TooLong(List<VisitorIssue> issues, string field, string? value, int max)
    {
        if (value is not null && value.Trim().Length > max)
            issues.Add(Error(field, $"Too long (maximum {max} characters)."));
    }

    private static VisitorIssue Error(string field, string message) => new(field, message, VisitorIssueSeverity.Error);
    private static VisitorIssue Warning(string field, string message) => new(field, message, VisitorIssueSeverity.Warning);
}
