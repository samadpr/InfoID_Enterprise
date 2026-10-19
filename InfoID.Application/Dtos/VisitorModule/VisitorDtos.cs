using InfoID.Domain.Common.Enums;

namespace InfoID.Application.Dtos.VisitorModule;

/// <summary>A visitor registration as shown in the Visitors list, with its print status
/// (taken from Print history: cards issued to this visitor ID).</summary>
public sealed class VisitorDto
{
    public long Id { get; set; }
    public string VisitorCode { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Company { get; set; }
    public string? PersonToMeet { get; set; }
    public string? Purpose { get; set; }
    public DateOnly? VisitDate { get; set; }
    /// <summary>Relative to InfoID's Assets folder, e.g. "Visitors/3f2a….jpg".</summary>
    public string? PhotoPath { get; set; }
    public Dictionary<string, string> ExtraFields { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public VisitorSource Source { get; set; }
    public string? SourceName { get; set; }
    public DateTime CreatedUtc { get; set; }

    /// <summary>Badges printed for this visitor ID (0 = not printed yet).</summary>
    public int PrintCount { get; set; }
    public DateTime? LastPrintedUtc { get; set; }
    public bool IsPrinted => PrintCount > 0;
}

/// <summary>The editable fields of a visitor (Add / Edit / one import row).</summary>
public class VisitorInput
{
    public string FullName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Company { get; set; }
    public string? PersonToMeet { get; set; }
    public string? Purpose { get; set; }
    public DateOnly? VisitDate { get; set; }
    public string? PhotoPath { get; set; }
    public Dictionary<string, string> ExtraFields { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>One row to import, with its identity at the source (duplicate check).</summary>
public sealed class VisitorImportItem : VisitorInput
{
    public string? SourceKey { get; set; }
}

public sealed class VisitorImportRequest
{
    public VisitorSource Source { get; set; } = VisitorSource.File;
    /// <summary>File name or form name, for display.</summary>
    public string? SourceName { get; set; }
    public List<VisitorImportItem> Items { get; set; } = new();
}

public sealed class VisitorImportResult
{
    public int Imported { get; set; }
    /// <summary>Rows skipped because the same registration was imported before.</summary>
    public int SkippedDuplicates { get; set; }
    /// <summary>Rows skipped because they still had errors.</summary>
    public int SkippedInvalid { get; set; }
    public Guid BatchKey { get; set; }
    public List<VisitorDto> Created { get; set; } = new();
}

public enum VisitorPrintFilter { All, NotPrinted, Printed }

public enum VisitorPeriod { Today, Upcoming, Last7Days, Last30Days, All }

public sealed class VisitorFilter
{
    public VisitorPeriod Period { get; set; } = VisitorPeriod.All;
    public VisitorPrintFilter PrintStatus { get; set; } = VisitorPrintFilter.All;
    /// <summary>Matches visitor ID, name, phone, email, company, person to meet.</summary>
    public string? Search { get; set; }
    /// <summary>Local "today", passed in so results don't depend on the server clock.</summary>
    public DateOnly Today { get; set; } = DateOnly.FromDateTime(DateTime.Today);
}

/// <summary>Visitor Management settings kept in the InfoID database (so Backup covers them).</summary>
public sealed class VisitorSettingsDto
{
    /// <summary>The design used for visitor badges; null = ask each time.</summary>
    public long? BadgeTemplateId { get; set; }
}

public enum VisitorIssueSeverity { Warning, Error }

/// <summary>A problem with one field of a visitor (shown in the import review / edit dialog).</summary>
public sealed record VisitorIssue(string Field, string Message, VisitorIssueSeverity Severity);
