using InfoID.Domain.Common;
using InfoID.Domain.Common.Enums;

namespace InfoID.Domain.Entities.VisitorModule;

/// <summary>
/// One visitor registration (Visitor Management, SRS 4.12): a Google Form response, a row
/// of a bulk-upload file, or a manual entry. One record per registration -- a returning
/// visitor simply registers again. Used to print visitor badges through the Card Designer;
/// no check-in/check-out tracking (agreed scope, Oct 2026).
///
/// Personal data: records are hard-deleted (not soft-deleted) when the user deletes them,
/// and the photo file is removed with them.
/// </summary>
public class Visitor : BaseEntity
{
    /// <summary>Visible visitor ID printed on the badge and in its QR code, e.g. V-000231.
    /// Unique and never reused (taken from a running counter).</summary>
    public string VisitorCode { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Company { get; set; }

    /// <summary>The person the visitor comes to meet (free text, as entered in the form).</summary>
    public string? PersonToMeet { get; set; }

    public string? Purpose { get; set; }
    public DateOnly? VisitDate { get; set; }

    /// <summary>Photo file relative to InfoID's Assets folder (e.g. "Visitors/V-000231.jpg");
    /// null when the visitor has no photo.</summary>
    public string? PhotoPath { get; set; }

    /// <summary>Answers to extra form questions / extra file columns, as a JSON object
    /// (column name -> text). Available to the badge design like the core fields.</summary>
    public string? ExtraFieldsJson { get; set; }

    public VisitorSource Source { get; set; }

    /// <summary>Where it came from, for display: the file name or form name.</summary>
    public string? SourceName { get; set; }

    /// <summary>Identity of the registration at its source (form response id, or a key built
    /// from the row), so the same registration is never imported twice. Null for manual entries.</summary>
    public string? SourceKey { get; set; }

    /// <summary>Groups the visitors imported together in one import.</summary>
    public Guid? ImportBatchKey { get; set; }
}
