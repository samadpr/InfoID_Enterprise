namespace InfoID.Application.Reporting;

/// <summary>The predefined reports (SRS 4.11).</summary>
public enum ReportKind
{
    /// <summary>FR-REP-1: every card issued in a period, by design.</summary>
    CardIssuance,
    /// <summary>FR-REP-2: every print run, including failures and reprints.</summary>
    PrintLog,
    /// <summary>FR-REP-3: cards expiring within a lead time, and expired cards still in use.</summary>
    ExpiringCards,
    /// <summary>FR-REP-3: replacement cards with the reason (lost, damaged, renewed).</summary>
    ReplacementCards,
    /// <summary>FR-REP-4: the audit log -- who did what, when, with before/after values.</summary>
    AuditLog,
}

/// <summary>Print log filter.</summary>
public enum PrintLogFilter { All, FailedOnly, ReprintsOnly }

public enum ReportColumnType { Text, Number, Date, DateTime }

/// <summary>A report column. <see cref="Width"/> is a relative weight for PDF layout.</summary>
public sealed record ReportColumn(string Header, ReportColumnType Type = ReportColumnType.Text, double Width = 1);

/// <summary>What to report on. Dates are the operator's local dates (inclusive); the
/// service converts them with <see cref="UtcOffset"/> because the database stores UTC.</summary>
public class ReportRequest
{
    public ReportKind Kind { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public TimeSpan UtcOffset { get; set; }
    public DateTime TodayLocalDate { get; set; } = DateTime.Today;

    /// <summary>Only this design (PrintJob.DesignName); null = all designs.</summary>
    public string? DesignName { get; set; }

    public PrintLogFilter PrintLogFilter { get; set; } = PrintLogFilter.All;

    /// <summary>Expiring cards: lead time in days.</summary>
    public int ExpiringWithinDays { get; set; } = 30;

    /// <summary>Expiring cards: also list cards already past their expiry.</summary>
    public bool IncludeExpired { get; set; } = true;

    /// <summary>Organization name for the report header (optional).</summary>
    public string? OrganizationName { get; set; }

    /// <summary>Audit log: only this area (AuditModules); null = all.</summary>
    public string? AuditModule { get; set; }

    /// <summary>Audit log: text to look for in description, user and values.</summary>
    public string? SearchText { get; set; }

    /// <summary>Audit log: result of the integrity check, printed in the summary.</summary>
    public string? AuditIntegrityText { get; set; }
}

/// <summary>A finished report: a title, the filters it used, typed rows and a summary.
/// Rendered on screen and exported to Excel, PDF and CSV from the same object, so the
/// three formats always match.</summary>
public class ReportTable
{
    public string Title { get; set; } = string.Empty;

    /// <summary>The filters, in words ("1 Oct 2026 to 31 Oct 2026 · All designs").</summary>
    public string Subtitle { get; set; } = string.Empty;

    public string? OrganizationName { get; set; }
    public DateTime GeneratedAt { get; set; } = DateTime.Now;

    public List<ReportColumn> Columns { get; } = new();

    /// <summary>Cell values: string, int, DateTime (local) or null; one array per row,
    /// same length as <see cref="Columns"/>.</summary>
    public List<object?[]> Rows { get; } = new();

    /// <summary>Label/value pairs shown above the table ("Total cards", "128").</summary>
    public List<KeyValuePair<string, string>> Summary { get; } = new();

    /// <summary>A short file name for exports, e.g. "Card issuance 2026-10-01 to 2026-10-31".</summary>
    public string FileNameStem { get; set; } = "InfoID report";

    public static string FormatCell(object? value, ReportColumnType type) => value switch
    {
        null => string.Empty,
        DateTime d when type == ReportColumnType.DateTime => d.ToString("dd MMM yyyy HH:mm"),
        DateTime d => d.ToString("dd MMM yyyy"),
        int i => i.ToString("N0"),
        long l => l.ToString("N0"),
        _ => value.ToString() ?? string.Empty,
    };
}
