using System.ComponentModel.DataAnnotations;
using System.Globalization;
using InfoID.LicenseManager.Application.DTOs.Audit;

namespace InfoID.LicenseManager.Web.ViewModels.Audit;

/// <summary>Query-string filters of the audit log (all optional). Dates are UTC days.</summary>
public sealed class AuditQuery
{
    [DataType(DataType.Date)]
    public DateOnly? From { get; set; }

    [DataType(DataType.Date)]
    public DateOnly? To { get; set; }

    [StringLength(100)]
    public string? Who { get; set; }

    /// <summary>Part of the action name (query key "event", so it never clashes with the MVC route key "action").</summary>
    [StringLength(100)]
    public string? Event { get; set; }

    public bool IsFiltered => From is not null || To is not null || !string.IsNullOrWhiteSpace(Who) || !string.IsNullOrWhiteSpace(Event);

    public AuditSearchDto ToSearch() => new(From, To, Who, Event, AuditLogViewModel.PageLimit);
}

public sealed class AuditLogViewModel
{
    public const int PageLimit = 500;

    public AuditLogViewModel(IEnumerable<AuditLogEntryDto> entries, AuditQuery query, string? chainMessage, bool chainIsIntact)
    {
        Rows = entries.Select(e => new AuditRowViewModel(
            e.Id,
            e.ActionDateUtc.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture),
            e.ActionDateUtc.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
            e.AdminName ?? "-",
            e.Action,
            e.IpAddress ?? "-",
            e.DetailsJson ?? "")).ToList();
        Query = query;
        ChainMessage = chainMessage;
        ChainIsIntact = chainIsIntact;
    }

    public IReadOnlyList<AuditRowViewModel> Rows { get; }

    public AuditQuery Query { get; }

    /// <summary>True when the list was cut at <see cref="PageLimit"/> entries, so older matches exist.</summary>
    public bool MayBeTruncated => Rows.Count >= PageLimit;

    /// <summary>Result of the last "verify chain" run, if any.</summary>
    public string? ChainMessage { get; }

    public bool ChainIsIntact { get; }
}

public sealed record AuditRowViewModel(long Id, string When, string WhenSort, string Who, string Action, string Address, string Details);
