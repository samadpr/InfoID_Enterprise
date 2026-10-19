using InfoID.Application.Interfaces;
using InfoID.Application.Reporting;
using InfoID.Domain.Common.Enums;
using InfoID.Domain.Common.Interfaces;
using InfoID.Domain.Entities.ApprovalAuditModule;
using InfoID.Domain.Entities.CardholderModule;
using InfoID.Domain.Entities.IssuanceModule;
using InfoID.Domain.Entities.PrintingModule;

namespace InfoID.Application.Services;

/// <summary>
/// Predefined reports (SRS 4.11) from the tables Print history fills:
///   Card issuance (FR-REP-1)     Card + Cardholder + the PrintJob that printed it
///   Print log (FR-REP-2)         PrintJob (+ PrintJobItem for reprint counts)
///   Expiring cards (FR-REP-3)    Issued/active cards by expiry date
///   Replacement cards (FR-REP-3) Cards that replaced another, with the reason
/// Read-only. Times are stored as UTC and shown in the operator's local time.
/// </summary>
public class ReportService : IReportService
{
    private const string GenericCardholderRef = "__GENERIC_CARD__";
    private readonly IUnitOfWork _uow;

    public ReportService(IUnitOfWork uow) => _uow = uow;

    public async Task<IReadOnlyList<string>> GetDesignNamesAsync(CancellationToken ct = default)
    {
        var jobs = await _uow.Repository<PrintJob>().FindAsync(j => j.DesignName != null, ct);
        return jobs.Select(j => j.DesignName!).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n).ToList();
    }

    public Task<ReportTable> GenerateAsync(ReportRequest request, CancellationToken ct = default) => request.Kind switch
    {
        ReportKind.CardIssuance => CardIssuanceAsync(request, ct),
        ReportKind.PrintLog => PrintLogAsync(request, ct),
        ReportKind.ExpiringCards => ExpiringCardsAsync(request, ct),
        ReportKind.ReplacementCards => ReplacementCardsAsync(request, ct),
        ReportKind.AuditLog => AuditLogAsync(request, ct),
        _ => throw new ArgumentOutOfRangeException(nameof(request), request.Kind, "Unknown report"),
    };

    // ------------------------------------------------------------------ FR-REP-1 ----

    private async Task<ReportTable> CardIssuanceAsync(ReportRequest r, CancellationToken ct)
    {
        var (fromUtc, toUtc) = UtcRange(r);
        var cards = (await _uow.Repository<Card>().FindAsync(c =>
                c.IssuedDate != null && c.IssuedDate >= fromUtc && c.IssuedDate < toUtc, ct))
            .OrderBy(c => c.IssuedDate).ThenBy(c => c.Id).ToList();

        var ctx = await LoadCardContextAsync(cards, ct);
        var table = NewTable(r, "Card issuance report", "Card issuance");
        table.Columns.AddRange(new[]
        {
            new ReportColumn("Issued", ReportColumnType.DateTime, 1.3),
            new ReportColumn("Card no.", ReportColumnType.Text, 1),
            new ReportColumn("Name", ReportColumnType.Text, 2),
            new ReportColumn("Person ID", ReportColumnType.Text, 1),
            new ReportColumn("Design", ReportColumnType.Text, 1.5),
            new ReportColumn("Type", ReportColumnType.Text, 1.1),
            new ReportColumn("Expires", ReportColumnType.Date, 1),
            new ReportColumn("Status", ReportColumnType.Text, 0.9),
        });

        int newCards = 0, reprints = 0;
        var byDesign = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var card in cards)
        {
            var design = ctx.DesignOf(card.Id);
            if (!MatchesDesign(r, design)) continue;

            var reason = ctx.ReprintReasonOf(card.Id);
            var isReprint = card.ReplacesCardId != null || reason != null;
            if (isReprint) reprints++; else newCards++;
            byDesign[design] = byDesign.TryGetValue(design, out var n) ? n + 1 : 1;

            table.Rows.Add(new object?[]
            {
                ToLocal(card.IssuedDate!.Value, r),
                card.CardNumber,
                ctx.NameOf(card),
                ctx.PersonKeyOf(card),
                design,
                isReprint ? "Reprint" + (reason is null ? "" : $" ({ReasonText(reason)})") : "New",
                card.ExpiryDate?.Date,
                card.Status.ToString(),
            });
        }

        table.Summary.Add(Pair("Cards issued", table.Rows.Count));
        table.Summary.Add(Pair("New cards", newCards));
        table.Summary.Add(Pair("Reprints", reprints));
        foreach (var d in byDesign.OrderByDescending(d => d.Value).Take(5))
            table.Summary.Add(new(d.Key, d.Value.ToString("N0")));
        return table;
    }

    // ------------------------------------------------------------------ FR-REP-2 ----

    private async Task<ReportTable> PrintLogAsync(ReportRequest r, CancellationToken ct)
    {
        var (fromUtc, toUtc) = UtcRange(r);
        var jobs = (await _uow.Repository<PrintJob>().FindAsync(j => j.CreatedDate >= fromUtc && j.CreatedDate < toUtc, ct))
            .Where(j => MatchesDesign(r, j.DesignName ?? ""))
            .OrderBy(j => j.CreatedDate).ToList();

        var printerIds = jobs.Select(j => j.PrinterProfileId).Distinct().ToList();
        var printers = printerIds.Count == 0
            ? new Dictionary<long, string>()
            : (await _uow.Repository<PrinterProfile>().FindAsync(p => printerIds.Contains(p.Id), ct)).ToDictionary(p => p.Id, p => p.Name);
        var jobIds = jobs.Select(j => j.Id).ToList();
        var items = jobIds.Count == 0
            ? new List<PrintJobItem>()
            : (await _uow.Repository<PrintJobItem>().FindAsync(i => jobIds.Contains(i.PrintJobId), ct)).ToList();
        var reprintsByJob = items.Where(i => i.ReprintReasonCode != null)
            .GroupBy(i => i.PrintJobId).ToDictionary(g => g.Key, g => g.ToList());
        var recordsByJob = items.GroupBy(i => i.PrintJobId).ToDictionary(g => g.Key, g => g.Count());

        var title = r.PrintLogFilter switch
        {
            PrintLogFilter.FailedOnly => "Print log: failed runs",
            PrintLogFilter.ReprintsOnly => "Print log: reprints",
            _ => "Print log",
        };
        var table = NewTable(r, title, "Print log");
        table.Columns.AddRange(new[]
        {
            new ReportColumn("Date / time", ReportColumnType.DateTime, 1.3),
            new ReportColumn("Design", ReportColumnType.Text, 1.5),
            new ReportColumn("Printer", ReportColumnType.Text, 1.5),
            new ReportColumn("Type", ReportColumnType.Text, 0.8),
            new ReportColumn("Records", ReportColumnType.Number, 0.7),
            new ReportColumn("Cards", ReportColumnType.Number, 0.7),
            new ReportColumn("Reprints", ReportColumnType.Text, 1.4),
            new ReportColumn("Result", ReportColumnType.Text, 0.8),
            new ReportColumn("Failure reason", ReportColumnType.Text, 1.8),
        });

        int printed = 0, failed = 0, cards = 0, reprintCount = 0;
        foreach (var job in jobs)
        {
            reprintsByJob.TryGetValue(job.Id, out var jobReprints);
            var isFailed = job.Status == PrintJobStatus.Failed;
            if (r.PrintLogFilter == PrintLogFilter.FailedOnly && !isFailed) continue;
            if (r.PrintLogFilter == PrintLogFilter.ReprintsOnly && jobReprints is not { Count: > 0 }) continue;

            if (isFailed) failed++; else { printed++; cards += job.TotalCards; }
            reprintCount += jobReprints?.Count ?? 0;

            var reprintText = jobReprints is { Count: > 0 }
                ? $"{jobReprints.Count}: " + string.Join(", ", jobReprints.GroupBy(i => i.ReprintReasonCode)
                    .Select(g => $"{ReasonText(g.Key)} {g.Count()}"))
                : "";

            table.Rows.Add(new object?[]
            {
                ToLocal(job.CreatedDate, r),
                job.DesignName ?? "",
                printers.TryGetValue(job.PrinterProfileId, out var p) ? p : "",
                job.JobType.ToString(),
                recordsByJob.TryGetValue(job.Id, out var rc) ? rc : 0,
                job.TotalCards,
                reprintText,
                job.Status switch { PrintJobStatus.Completed => "Printed", PrintJobStatus.Failed => "Failed", _ => job.Status.ToString() },
                job.FailureReason ?? "",
            });
        }

        table.Summary.Add(Pair("Print runs", table.Rows.Count));
        table.Summary.Add(Pair("Printed", printed));
        table.Summary.Add(Pair("Failed", failed));
        table.Summary.Add(Pair("Cards printed", cards));
        table.Summary.Add(Pair("Reprinted cards", reprintCount));
        return table;
    }

    // ------------------------------------------------------------------ FR-REP-3 ----

    private async Task<ReportTable> ExpiringCardsAsync(ReportRequest r, CancellationToken ct)
    {
        var today = r.TodayLocalDate.Date;
        var horizon = today.AddDays(Math.Max(0, r.ExpiringWithinDays));
        var includeExpired = r.IncludeExpired;
        var cards = (await _uow.Repository<Card>().FindAsync(c =>
                (c.Status == CardStatus.Issued || c.Status == CardStatus.Active) &&
                c.ExpiryDate != null && c.ExpiryDate <= horizon && (includeExpired || c.ExpiryDate >= today), ct))
            .OrderBy(c => c.ExpiryDate).ThenBy(c => c.Id).ToList();

        var ctx = await LoadCardContextAsync(cards, ct);
        var table = new ReportTable
        {
            Title = "Expiring cards report",
            Subtitle = $"Cards in use that expire by {horizon:dd MMM yyyy} ({r.ExpiringWithinDays} days)" +
                       (includeExpired ? ", including cards already expired" : "") +
                       $" · {DesignText(r)}",
            OrganizationName = r.OrganizationName,
            FileNameStem = $"Expiring cards {today:yyyy-MM-dd}",
        };
        table.Columns.AddRange(new[]
        {
            new ReportColumn("Name", ReportColumnType.Text, 2),
            new ReportColumn("Person ID", ReportColumnType.Text, 1),
            new ReportColumn("Card no.", ReportColumnType.Text, 1),
            new ReportColumn("Design", ReportColumnType.Text, 1.5),
            new ReportColumn("Issued", ReportColumnType.Date, 1),
            new ReportColumn("Expires", ReportColumnType.Date, 1),
            new ReportColumn("Days left", ReportColumnType.Number, 0.8),
            new ReportColumn("State", ReportColumnType.Text, 0.9),
        });

        int expired = 0, expiring = 0;
        foreach (var card in cards)
        {
            var design = ctx.DesignOf(card.Id);
            if (!MatchesDesign(r, design)) continue;
            var expiry = card.ExpiryDate!.Value.Date;
            var days = (int)(expiry - today).TotalDays;
            if (days < 0) expired++; else expiring++;
            table.Rows.Add(new object?[]
            {
                ctx.NameOf(card),
                ctx.PersonKeyOf(card),
                card.CardNumber,
                design,
                card.IssuedDate is { } issued ? ToLocal(issued, r).Date : null,
                expiry,
                days,
                days < 0 ? "Expired" : days == 0 ? "Expires today" : "Expiring",
            });
        }

        table.Summary.Add(Pair("Cards listed", table.Rows.Count));
        table.Summary.Add(Pair("Already expired", expired));
        table.Summary.Add(Pair($"Expiring within {r.ExpiringWithinDays} days", expiring));
        return table;
    }

    private async Task<ReportTable> ReplacementCardsAsync(ReportRequest r, CancellationToken ct)
    {
        var (fromUtc, toUtc) = UtcRange(r);
        var cards = (await _uow.Repository<Card>().FindAsync(c =>
                c.ReplacesCardId != null && c.IssuedDate != null && c.IssuedDate >= fromUtc && c.IssuedDate < toUtc, ct))
            .OrderBy(c => c.IssuedDate).ToList();

        var ctx = await LoadCardContextAsync(cards, ct);
        var oldIds = cards.Select(c => c.ReplacesCardId!.Value).Distinct().ToList();
        var oldCards = oldIds.Count == 0
            ? new Dictionary<long, Card>()
            : (await _uow.Repository<Card>().FindAsync(c => oldIds.Contains(c.Id), ct)).ToDictionary(c => c.Id);
        var replacedNotes = oldIds.Count == 0
            ? new Dictionary<long, string?>()
            : (await _uow.Repository<CardStatusHistory>().FindAsync(h => oldIds.Contains(h.CardId) && h.ToStatus == CardStatus.Replaced, ct))
                .GroupBy(h => h.CardId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(h => h.ChangedDate).First().Reason);

        var table = NewTable(r, "Replacement cards report", "Replacement cards");
        table.Columns.AddRange(new[]
        {
            new ReportColumn("Date", ReportColumnType.DateTime, 1.3),
            new ReportColumn("Name", ReportColumnType.Text, 2),
            new ReportColumn("Person ID", ReportColumnType.Text, 1),
            new ReportColumn("New card", ReportColumnType.Text, 1),
            new ReportColumn("Replaced card", ReportColumnType.Text, 1),
            new ReportColumn("Reason", ReportColumnType.Text, 0.9),
            new ReportColumn("Note", ReportColumnType.Text, 2),
            new ReportColumn("Design", ReportColumnType.Text, 1.4),
        });

        var byReason = new Dictionary<string, int>();
        foreach (var card in cards)
        {
            var design = ctx.DesignOf(card.Id);
            if (!MatchesDesign(r, design)) continue;
            var reason = ReasonText(ctx.ReprintReasonOf(card.Id));
            byReason[reason] = byReason.TryGetValue(reason, out var n) ? n + 1 : 1;
            oldCards.TryGetValue(card.ReplacesCardId!.Value, out var old);
            replacedNotes.TryGetValue(card.ReplacesCardId!.Value, out var history);

            table.Rows.Add(new object?[]
            {
                ToLocal(card.IssuedDate!.Value, r),
                ctx.NameOf(card),
                ctx.PersonKeyOf(card),
                card.CardNumber,
                old?.CardNumber ?? "",
                reason,
                NoteFromHistory(history),
                design,
            });
        }

        table.Summary.Add(Pair("Replacement cards", table.Rows.Count));
        foreach (var reason in byReason.OrderByDescending(x => x.Value))
            table.Summary.Add(Pair(reason.Key, reason.Value));
        return table;
    }

    // ------------------------------------------------------------------ helpers ----

    /// <summary>Who each card belongs to and which print run (design) produced it.</summary>
    private sealed class CardContext
    {
        public Dictionary<long, Cardholder> Holders { get; init; } = new();
        public Dictionary<long, PrintJobItem> ItemByCard { get; init; } = new();
        public Dictionary<long, PrintJob> Jobs { get; init; } = new();

        public string DesignOf(long cardId) =>
            ItemByCard.TryGetValue(cardId, out var item) && Jobs.TryGetValue(item.PrintJobId, out var job)
                ? job.DesignName ?? ""
                : "";

        public ReprintReasonCode? ReprintReasonOf(long cardId) =>
            ItemByCard.TryGetValue(cardId, out var item) ? item.ReprintReasonCode : null;

        public string NameOf(Card card) => Holders.TryGetValue(card.CardholderId, out var h) ? h.FullName : "";

        public string PersonKeyOf(Card card) =>
            Holders.TryGetValue(card.CardholderId, out var h) && h.ExternalRefId is { } key && key != GenericCardholderRef ? key : "";
    }

    private async Task<CardContext> LoadCardContextAsync(IReadOnlyCollection<Card> cards, CancellationToken ct)
    {
        if (cards.Count == 0) return new CardContext();
        var cardIds = cards.Select(c => c.Id).ToList();
        var holderIds = cards.Select(c => c.CardholderId).Distinct().ToList();
        var holders = (await _uow.Repository<Cardholder>().FindAsync(h => holderIds.Contains(h.Id), ct)).ToDictionary(h => h.Id);
        var items = (await _uow.Repository<PrintJobItem>().FindAsync(i => cardIds.Contains(i.CardId), ct))
            .GroupBy(i => i.CardId).ToDictionary(g => g.Key, g => g.OrderBy(i => i.Id).First());
        var jobIds = items.Values.Select(i => i.PrintJobId).Distinct().ToList();
        var jobs = jobIds.Count == 0
            ? new Dictionary<long, PrintJob>()
            : (await _uow.Repository<PrintJob>().FindAsync(j => jobIds.Contains(j.Id), ct)).ToDictionary(j => j.Id);
        return new CardContext { Holders = holders, ItemByCard = items, Jobs = jobs };
    }

    /// <summary>Local inclusive date range to a UTC half-open range [from, to).</summary>
    private static (DateTime FromUtc, DateTime ToUtc) UtcRange(ReportRequest r)
    {
        var from = r.FromDate?.Date is { } f ? DateTime.SpecifyKind(f - r.UtcOffset, DateTimeKind.Utc) : DateTime.MinValue;
        var to = r.ToDate?.Date is { } t ? DateTime.SpecifyKind(t.AddDays(1) - r.UtcOffset, DateTimeKind.Utc) : DateTime.MaxValue;
        return (from, to);
    }

    private static DateTime ToLocal(DateTime utc, ReportRequest r) =>
        DateTime.SpecifyKind(DateTime.SpecifyKind(utc, DateTimeKind.Utc) + r.UtcOffset, DateTimeKind.Unspecified);

    private static bool MatchesDesign(ReportRequest r, string design) =>
        string.IsNullOrWhiteSpace(r.DesignName) || string.Equals(design, r.DesignName.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string DesignText(ReportRequest r) =>
        string.IsNullOrWhiteSpace(r.DesignName) ? "All designs" : $"Design: {r.DesignName.Trim()}";

    private static string PeriodText(ReportRequest r) => (r.FromDate, r.ToDate) switch
    {
        (null, null) => "All dates",
        ({ } f, null) => $"From {f:dd MMM yyyy}",
        (null, { } t) => $"Up to {t:dd MMM yyyy}",
        ({ } f, { } t) when f.Date == t.Date => $"{f:dd MMM yyyy}",
        ({ } f, { } t) => $"{f:dd MMM yyyy} to {t:dd MMM yyyy}",
    };

    private static ReportTable NewTable(ReportRequest r, string title, string fileTitle)
    {
        var period = (r.FromDate, r.ToDate) switch
        {
            ({ } f, { } t) => $" {f:yyyy-MM-dd} to {t:yyyy-MM-dd}",
            ({ } f, null) => $" from {f:yyyy-MM-dd}",
            (null, { } t) => $" to {t:yyyy-MM-dd}",
            _ => " all dates",
        };
        return new ReportTable
        {
            Title = title,
            Subtitle = $"{PeriodText(r)} · {DesignText(r)}",
            OrganizationName = r.OrganizationName,
            FileNameStem = fileTitle + period,
        };
    }

    private static string ReasonText(ReprintReasonCode? reason) => reason switch
    {
        ReprintReasonCode.Lost => "Lost",
        ReprintReasonCode.Damaged => "Damaged",
        ReprintReasonCode.Renewed => "Renewed",
        ReprintReasonCode.Other => "Other",
        _ => "Reprint",
    };

    /// <summary>PrintHistoryService stores "Lost: note (new card C-000011)"; keep the note.</summary>
    private static string NoteFromHistory(string? history)
    {
        if (string.IsNullOrWhiteSpace(history)) return "";
        var text = history;
        var cut = text.LastIndexOf(" (new card ", StringComparison.Ordinal);
        if (cut >= 0) text = text[..cut];
        var colon = text.IndexOf(": ", StringComparison.Ordinal);
        return colon >= 0 ? text[(colon + 2)..].Trim() : "";
    }

    private static KeyValuePair<string, string> Pair(string label, int value) => new(label, value.ToString("N0"));

    // ------------------------------------------------------------------ FR-REP-4 ----

    private async Task<ReportTable> AuditLogAsync(ReportRequest r, CancellationToken ct)
    {
        var (fromUtc, toUtc) = UtcRange(r);
        var module = string.IsNullOrWhiteSpace(r.AuditModule) ? null : r.AuditModule.Trim();
        var entries = (await _uow.Repository<AuditLog>().FindAsync(a =>
                a.ActionDate >= fromUtc && a.ActionDate < toUtc && (module == null || a.Module == module), ct))
            .OrderBy(a => a.Id)
            .ToList();

        if (!string.IsNullOrWhiteSpace(r.SearchText))
        {
            var search = r.SearchText.Trim();
            entries = entries.Where(a =>
                    Contains(a.Description, search) || Contains(a.CreatedBy, search) || Contains(a.Action, search)
                    || Contains(a.BeforeValueJson, search) || Contains(a.AfterValueJson, search))
                .ToList();
        }

        var table = NewTable(r, "Audit log", "Audit log");
        var filters = new List<string> { PeriodText(r), module is null ? "All areas" : $"Area: {module}" };
        if (!string.IsNullOrWhiteSpace(r.SearchText)) filters.Add($"Search: \"{r.SearchText.Trim()}\"");
        table.Subtitle = string.Join(" · ", filters);
        table.Columns.AddRange(new[]
        {
            new ReportColumn("#", ReportColumnType.Number, 0.45),
            new ReportColumn("Date / time", ReportColumnType.DateTime, 1.15),
            new ReportColumn("User", ReportColumnType.Text, 1.3),
            new ReportColumn("Computer", ReportColumnType.Text, 0.9),
            new ReportColumn("Area", ReportColumnType.Text, 0.75),
            new ReportColumn("What happened", ReportColumnType.Text, 2.6),
            new ReportColumn("Details (before → after)", ReportColumnType.Text, 3.0),
        });

        foreach (var a in entries)
        {
            table.Rows.Add(new object?[]
            {
                (int)a.Id,
                ToLocal(a.ActionDate, r),
                a.CreatedBy ?? "",
                a.MachineName ?? "",
                a.Module,
                a.Description ?? a.Action,
                AuditFormatting.ChangesText(a.BeforeValueJson, a.AfterValueJson),
            });
        }

        table.Summary.Add(Pair("Entries", table.Rows.Count));
        table.Summary.Add(Pair("Users", entries.Select(a => a.CreatedBy ?? "").Distinct(StringComparer.OrdinalIgnoreCase).Count()));
        if (!string.IsNullOrWhiteSpace(r.AuditIntegrityText))
            table.Summary.Add(new KeyValuePair<string, string>("Integrity", r.AuditIntegrityText));
        return table;
    }

    private static bool Contains(string? text, string search) =>
        text is not null && text.Contains(search, StringComparison.OrdinalIgnoreCase);
}
