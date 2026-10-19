using InfoID.Application.Dtos.PrintingModule;
using InfoID.Application.Interfaces;
using InfoID.Domain.Common.Enums;
using InfoID.Domain.Common.Interfaces;
using InfoID.Domain.Entities.CardholderModule;
using InfoID.Domain.Entities.IssuanceModule;
using InfoID.Domain.Entities.OrganizationModule;
using InfoID.Domain.Entities.PrintingModule;
using InfoID.Domain.Entities.SystemModule;

namespace InfoID.Application.Services;

/// <summary>
/// Print history in the local database (FR-PRN-4, FR-PRN-6), using the Domain Model's
/// Printing and Issuance tables:
///
///   PrintJob        one per print run (design, printer, counts, status, times)
///   PrinterProfile  created automatically the first time a printer is used
///   Cardholder      the person a card is for -- matched by the Person ID column
///                   (Cardholder.ExternalRefId) among cards of the SAME design, so
///                   "ID 1001" on a staff card and a visitor card stay different people
///   Card            one per printed record: sequential card number, Issued, expiry
///   PrintJobItem    links job and card; carries the reprint reason
///   CardStatusHistory  Issued for new cards; Replaced (with reason) for the old card
///                   when a person is printed again
///
/// Static designs (no database data) are recorded as generic cards under one shared
/// "Generic card" cardholder and are never treated as reprints. A failed run is
/// recorded as a Failed job without cards. Copies of the same record are physical
/// cards (PrintJob.TotalCards) but one Card row (they're identical).
/// </summary>
public class PrintHistoryService : IPrintHistoryService
{
    private const string LastCardNumberKey = "Issuance.LastCardNumber";
    private const string GenericCardholderRef = "__GENERIC_CARD__";

    private readonly IUnitOfWork _uow;
    private readonly IAuditTrail? _audit;

    /// <param name="audit">Optional so existing callers/tests that construct the service
    /// directly still work; the app always supplies it (every print run is audited).</param>
    public PrintHistoryService(IUnitOfWork uow, IAuditTrail? audit = null)
    {
        _uow = uow;
        _audit = audit;
    }

    // ------------------------------------------------------------------ reprint check ----

    public async Task<IReadOnlyList<ExistingCardDto>> FindExistingCardsAsync(long? templateId, IReadOnlyCollection<string> personKeys, CancellationToken ct = default)
    {
        var keys = personKeys.Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k.Trim()).Distinct().ToList();
        if (keys.Count == 0) return Array.Empty<ExistingCardDto>();

        var active = await LoadActiveCardsByPersonAsync(templateId, keys, ct);
        return active.Select(pair => new ExistingCardDto
        {
            PersonKey = pair.Key,
            FullName = pair.Value.Holder.FullName,
            CardNumber = pair.Value.Card.CardNumber,
            IssuedDate = pair.Value.Card.IssuedDate,
        }).OrderBy(c => c.FullName).ToList();
    }

    /// <summary>Latest Issued/Active card per Person ID for this design.</summary>
    private async Task<Dictionary<string, (Card Card, Cardholder Holder)>> LoadActiveCardsByPersonAsync(
        long? templateId, List<string> keys, CancellationToken ct)
    {
        var holders = await _uow.Repository<Cardholder>().FindAsync(h => h.ExternalRefId != null && keys.Contains(h.ExternalRefId), ct);
        var result = new Dictionary<string, (Card, Cardholder)>(StringComparer.OrdinalIgnoreCase);
        if (holders.Count == 0) return result;

        var holderIds = holders.Select(h => h.Id).ToList();
        var cards = await _uow.Repository<Card>().FindAsync(c =>
            c.TemplateId == templateId &&
            holderIds.Contains(c.CardholderId) &&
            (c.Status == CardStatus.Issued || c.Status == CardStatus.Active), ct);

        var holderById = holders.ToDictionary(h => h.Id);
        foreach (var card in cards.OrderByDescending(c => c.IssuedDate ?? c.CreatedDate))
        {
            var holder = holderById[card.CardholderId];
            result.TryAdd(holder.ExternalRefId!, (card, holder));
        }
        return result;
    }

    // ------------------------------------------------------------------ record ----

    public async Task<RecordPrintJobResult> RecordJobAsync(RecordPrintJobRequest request, CancellationToken ct = default)
    {
        var result = await RecordJobCoreAsync(request, ct);
        if (_audit is not null) await AuditPrintAsync(request, result, ct);
        return result;
    }

    /// <summary>Audit trail (FR-REP-4): every print run -- what, where, how many, which
    /// card numbers, reprint reason, and failures.</summary>
    private Task AuditPrintAsync(RecordPrintJobRequest request, RecordPrintJobResult result, CancellationToken ct)
    {
        var design = string.IsNullOrWhiteSpace(request.DesignName) ? "Untitled design" : request.DesignName.Trim();
        var cards = request.Records.Count * Math.Max(1, request.CopiesPerRecord);
        var description = request.Succeeded
            ? $"Printed {cards} card{(cards == 1 ? "" : "s")} of '{design}' on {request.PrinterName}" +
              (result.Reprints > 0 ? $" ({result.Reprints} reprint{(result.Reprints == 1 ? "" : "s")}: {ReprintReasonText(request.ReprintReason, null)})" : "")
            : $"Printing '{design}' on {request.PrinterName} failed" +
              (string.IsNullOrWhiteSpace(request.FailureMessage) ? "" : ": " + request.FailureMessage.Trim());

        return _audit!.RecordAsync(new AuditEntry(AuditModules.Printing, request.Succeeded ? (result.Reprints > 0 ? "Reprinted" : "Printed") : "PrintFailed", description)
        {
            EntityName = "PrintJob",
            EntityId = result.PrintJobId,
            After = new
            {
                Design = design,
                request.TemplateId,
                Printer = request.PrinterName,
                Records = request.Records.Count,
                Copies = Math.Max(1, request.CopiesPerRecord),
                Cards = cards,
                result.NewCards,
                result.Reprints,
                ReprintReason = request.ReprintReason?.ToString(),
                request.ReprintNote,
                Result = request.Succeeded ? "Printed" : "Failed",
                FailureReason = request.Succeeded ? null : request.FailureMessage,
                CardNumbers = result.CardNumbers.Count <= 100 ? result.CardNumbers : result.CardNumbers.Take(100).Append($"... {result.CardNumbers.Count - 100} more").ToList(),
            },
        }, ct);
    }

    private async Task<RecordPrintJobResult> RecordJobCoreAsync(RecordPrintJobRequest request, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var organizationId = await EnsureOrganizationAsync(ct);
        var userId = await EnsureOperatorAsync(organizationId, ct);
        var printerId = await EnsurePrinterProfileAsync(organizationId, request.PrinterName, ct);
        var copies = Math.Max(1, request.CopiesPerRecord);
        var totalCards = request.Records.Count * copies;

        var job = new PrintJob
        {
            PrinterProfileId = printerId,
            TemplateId = request.TemplateId,
            RequestedByUserId = userId,
            DesignName = Truncate(string.IsNullOrWhiteSpace(request.DesignName) ? "Untitled design" : request.DesignName.Trim(), 200),
            JobType = request.Records.Count == 1 ? PrintJobType.Single : PrintJobType.Batch,
            Status = request.Succeeded ? PrintJobStatus.Completed : PrintJobStatus.Failed,
            TotalCards = totalCards,
            SuccessCount = request.Succeeded ? totalCards : 0,
            FailureCount = request.Succeeded ? 0 : totalCards,
            StartedDate = request.StartedUtc == default ? now : request.StartedUtc,
            CompletedDate = now,
            FailureReason = request.Succeeded || string.IsNullOrWhiteSpace(request.FailureMessage)
                ? null
                : Truncate(request.FailureMessage.Trim(), 500),
        };
        await _uow.Repository<PrintJob>().AddAsync(job, ct);

        var result = new RecordPrintJobResult();
        if (!request.Succeeded || request.Records.Count == 0)
        {
            await _uow.SaveChangesAsync(ct);
            result.PrintJobId = job.Id;
            return result;
        }

        // People who already have a card for this design (reprint candidates).
        var keys = request.IsDataBound
            ? request.Records.Select(r => r.PersonKey).Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k!.Trim()).Distinct().ToList()
            : new List<string>();
        // Looked up even without a reprint reason so a known person keeps one
        // Cardholder row; only a reason turns the old card into "Replaced".
        var existing = keys.Count > 0
            ? await LoadActiveCardsByPersonAsync(request.TemplateId, keys, ct)
            : new Dictionary<string, (Card Card, Cardholder Holder)>(StringComparer.OrdinalIgnoreCase);

        // Same person twice in one run, or new people: one Cardholder per key.
        var holdersThisRun = new Dictionary<string, Cardholder>(StringComparer.OrdinalIgnoreCase);
        var generic = request.IsDataBound ? null : await EnsureGenericCardholderAsync(organizationId, ct);
        var nextNumber = await GetLastCardNumberAsync(ct);

        var sequence = 0;
        foreach (var record in request.Records)
        {
            var key = request.IsDataBound && !string.IsNullOrWhiteSpace(record.PersonKey) ? record.PersonKey.Trim() : null;
            var name = Truncate(string.IsNullOrWhiteSpace(record.DisplayName) ? key ?? "Card" : record.DisplayName.Trim(), 200);

            Cardholder holder;
            Card? replaces = null;
            if (generic is not null)
            {
                holder = generic;
            }
            else if (key is not null && holdersThisRun.TryGetValue(key, out var seen))
            {
                holder = seen;
            }
            else if (key is not null && existing.TryGetValue(key, out var found))
            {
                holder = found.Holder;
                replaces = request.ReprintReason is null ? null : found.Card;
                if (!string.Equals(holder.FullName, name, StringComparison.Ordinal))
                {
                    holder.FullName = name;
                    holder.ModifiedDate = now;
                    _uow.Repository<Cardholder>().Update(holder);
                }
            }
            else
            {
                holder = new Cardholder
                {
                    OrganizationId = organizationId,
                    FullName = name,
                    ExternalRefId = key is null ? null : Truncate(key, 100),
                    IdNumber = key is null ? null : Truncate(key, 100),
                };
                await _uow.Repository<Cardholder>().AddAsync(holder, ct);
            }
            if (key is not null) holdersThisRun[key] = holder;

            // A Lost or Damaged card is replaced like-for-like: the new card keeps the
            // original expiry (a lost card isn't a renewal). Renewed/Other use the
            // validity setting. An expiry taken from the data is what's printed, so it wins.
            var expiryDate = record.ExpiryDate;
            if (replaces?.ExpiryDate is { } originalExpiry && !record.ExpiryFromData &&
                request.ReprintReason is ReprintReasonCode.Lost or ReprintReasonCode.Damaged)
            {
                expiryDate = originalExpiry;
            }

            nextNumber++;
            var card = new Card
            {
                Cardholder = holder,
                TemplateId = request.TemplateId,
                CardNumber = FormatCardNumber(nextNumber),
                Status = CardStatus.Issued,
                IssuedDate = now,
                ExpiryDate = expiryDate,
                ReplacesCard = replaces,
            };
            await _uow.Repository<Card>().AddAsync(card, ct);
            result.CardNumbers.Add(card.CardNumber);
            await _uow.Repository<CardStatusHistory>().AddAsync(new CardStatusHistory
            {
                Card = card,
                FromStatus = null,
                ToStatus = CardStatus.Issued,
                ChangedDate = now,
                Reason = replaces is null ? "Printed" : $"Reprint: {request.ReprintReason}",
                ChangedByUserId = userId,
            }, ct);

            if (replaces is not null)
            {
                var from = replaces.Status;
                replaces.Status = CardStatus.Replaced;
                replaces.ModifiedDate = now;
                _uow.Repository<Card>().Update(replaces);
                await _uow.Repository<CardStatusHistory>().AddAsync(new CardStatusHistory
                {
                    CardId = replaces.Id,
                    FromStatus = from,
                    ToStatus = CardStatus.Replaced,
                    ChangedDate = now,
                    Reason = Truncate(ReprintReasonText(request.ReprintReason, request.ReprintNote) + $" (new card {card.CardNumber})", 300),
                    ChangedByUserId = userId,
                }, ct);
                result.Reprints++;
            }
            else
            {
                result.NewCards++;
            }

            await _uow.Repository<PrintJobItem>().AddAsync(new PrintJobItem
            {
                PrintJob = job,
                Card = card,
                Sequence = ++sequence,
                Outcome = PrintOutcome.Success,
                ReprintReasonCode = replaces is null ? null : request.ReprintReason,
                PrintedDate = now,
            }, ct);
        }

        if (result.Reprints > 0 && result.NewCards == 0) job.JobType = PrintJobType.Reprint;

        await SetLastCardNumberAsync(nextNumber, ct);
        await _uow.SaveChangesAsync(ct);
        result.PrintJobId = job.Id;
        return result;
    }

    // ------------------------------------------------------------------ queries ----

    public async Task<IReadOnlyList<PrintJobSummaryDto>> GetJobsAsync(PrintJobFilter filter, CancellationToken ct = default)
    {
        var from = filter.FromUtc ?? DateTime.MinValue;
        var to = filter.ToUtc ?? DateTime.MaxValue;
        var jobs = (await _uow.Repository<PrintJob>().FindAsync(j => j.CreatedDate >= from && j.CreatedDate <= to, ct))
            .OrderByDescending(j => j.CreatedDate)
            .ToList();
        if (jobs.Count == 0) return Array.Empty<PrintJobSummaryDto>();

        var printerIds = jobs.Select(j => j.PrinterProfileId).Distinct().ToList();
        var printers = (await _uow.Repository<PrinterProfile>().FindAsync(p => printerIds.Contains(p.Id), ct)).ToDictionary(p => p.Id, p => p.Name);

        var jobIds = jobs.Select(j => j.Id).ToList();
        var items = await _uow.Repository<PrintJobItem>().FindAsync(i => jobIds.Contains(i.PrintJobId), ct);
        var itemsByJob = items.GroupBy(i => i.PrintJobId).ToDictionary(g => g.Key, g => g.ToList());

        // Search: design, printer, or a person (name / ID) printed in the job.
        HashSet<long>? jobsWithPerson = null;
        var text = filter.SearchText?.Trim();
        if (!string.IsNullOrEmpty(text))
        {
            var lower = text.ToLower();
            var holders = await _uow.Repository<Cardholder>().FindAsync(h =>
                h.FullName.ToLower().Contains(lower) || (h.ExternalRefId != null && h.ExternalRefId.ToLower().Contains(lower)), ct);
            var holderIds = holders.Select(h => h.Id).ToList();
            var cardIds = holderIds.Count == 0
                ? new List<long>()
                : (await _uow.Repository<Card>().FindAsync(c => holderIds.Contains(c.CardholderId), ct)).Select(c => c.Id).ToList();
            jobsWithPerson = items.Where(i => cardIds.Contains(i.CardId)).Select(i => i.PrintJobId).ToHashSet();
        }

        var result = new List<PrintJobSummaryDto>();
        foreach (var job in jobs)
        {
            var printer = printers.TryGetValue(job.PrinterProfileId, out var p) ? p : "";
            if (jobsWithPerson is not null &&
                !jobsWithPerson.Contains(job.Id) &&
                !(job.DesignName ?? "").Contains(text!, StringComparison.OrdinalIgnoreCase) &&
                !printer.Contains(text!, StringComparison.OrdinalIgnoreCase))
                continue;

            var jobItems = itemsByJob.TryGetValue(job.Id, out var list) ? list : new List<PrintJobItem>();
            result.Add(new PrintJobSummaryDto
            {
                Id = job.Id,
                CreatedDate = job.CreatedDate,
                CompletedDate = job.CompletedDate,
                DesignName = job.DesignName ?? "",
                PrinterName = printer,
                JobType = job.JobType,
                Status = job.Status,
                Records = jobItems.Count,
                TotalCards = job.TotalCards,
                SuccessCount = job.SuccessCount,
                FailureCount = job.FailureCount,
                ReprintCount = jobItems.Count(i => i.ReprintReasonCode is not null),
            });
            if (result.Count >= Math.Max(1, filter.MaxResults)) break;
        }
        return result;
    }

    public async Task<IReadOnlyList<PrintJobItemDto>> GetJobItemsAsync(long printJobId, CancellationToken ct = default)
    {
        var items = (await _uow.Repository<PrintJobItem>().FindAsync(i => i.PrintJobId == printJobId, ct)).OrderBy(i => i.Sequence).ToList();
        if (items.Count == 0) return Array.Empty<PrintJobItemDto>();

        var cardIds = items.Select(i => i.CardId).ToList();
        var cards = (await _uow.Repository<Card>().FindAsync(c => cardIds.Contains(c.Id), ct)).ToDictionary(c => c.Id);
        var holderIds = cards.Values.Select(c => c.CardholderId).Distinct().ToList();
        var holders = (await _uow.Repository<Cardholder>().FindAsync(h => holderIds.Contains(h.Id), ct)).ToDictionary(h => h.Id);

        return items.Select(i =>
        {
            cards.TryGetValue(i.CardId, out var card);
            Cardholder? holder = null;
            if (card is not null) holders.TryGetValue(card.CardholderId, out holder);
            return new PrintJobItemDto
            {
                Sequence = i.Sequence,
                CardNumber = card?.CardNumber ?? "",
                FullName = holder?.FullName ?? "",
                PersonKey = holder?.ExternalRefId == GenericCardholderRef ? null : holder?.ExternalRefId,
                Outcome = i.Outcome,
                ReprintReason = i.ReprintReasonCode,
                CardStatus = card?.Status ?? CardStatus.Issued,
                ExpiryDate = card?.ExpiryDate,
            };
        }).ToList();
    }

    // ------------------------------------------------------------------ helpers ----

    /// <summary>Same rule as CardDesignRepository: the first organization row, created
    /// as "My Organization" if none exists yet.</summary>
    private async Task<long> EnsureOrganizationAsync(CancellationToken ct)
    {
        var orgs = _uow.Repository<Organization>();
        var existing = (await orgs.GetAllAsync(ct)).OrderBy(o => o.CreatedDate).FirstOrDefault();
        if (existing is not null) return existing.Id;

        var org = new Organization { Name = "My Organization" };
        await orgs.AddAsync(org, ct);
        await _uow.SaveChangesAsync(ct);
        return org.Id;
    }

    /// <summary>PrintJob.RequestedByUserId is required. InfoID has one local operator
    /// profile (SRS 3.3); create it if activation/sign-up hasn't yet.</summary>
    private async Task<long> EnsureOperatorAsync(long organizationId, CancellationToken ct)
    {
        var users = _uow.Repository<AppUser>();
        var existing = (await users.GetAllAsync(ct)).OrderBy(u => u.CreatedDate).FirstOrDefault();
        if (existing is not null) return existing.Id;

        var user = new AppUser { OrganizationId = organizationId, FullName = "InfoID Operator" };
        await users.AddAsync(user, ct);
        await _uow.SaveChangesAsync(ct);
        return user.Id;
    }

    private async Task<long> EnsurePrinterProfileAsync(long organizationId, string printerName, CancellationToken ct)
    {
        var name = Truncate(string.IsNullOrWhiteSpace(printerName) ? "Unknown printer" : printerName.Trim(), 150);
        var profiles = _uow.Repository<PrinterProfile>();
        var existing = await profiles.SingleOrDefaultAsync(p => p.Name == name, ct);
        if (existing is not null) return existing.Id;

        var profile = new PrinterProfile
        {
            OrganizationId = organizationId,
            Name = name,
            Model = name,
            Brand = GuessBrand(name),
        };
        await profiles.AddAsync(profile, ct);
        await _uow.SaveChangesAsync(ct);
        return profile.Id;
    }

    private static PrinterBrand GuessBrand(string name)
    {
        foreach (var brand in new[] { PrinterBrand.Fargo, PrinterBrand.Evolis, PrinterBrand.Zebra, PrinterBrand.Magicard })
            if (name.Contains(brand.ToString(), StringComparison.OrdinalIgnoreCase)) return brand;
        return PrinterBrand.Other;
    }

    private async Task<Cardholder> EnsureGenericCardholderAsync(long organizationId, CancellationToken ct)
    {
        var repo = _uow.Repository<Cardholder>();
        var existing = await repo.SingleOrDefaultAsync(h => h.ExternalRefId == GenericCardholderRef, ct);
        if (existing is not null) return existing;

        var holder = new Cardholder { OrganizationId = organizationId, FullName = "Generic card", ExternalRefId = GenericCardholderRef };
        await repo.AddAsync(holder, ct);
        return holder;
    }

    private async Task<long> GetLastCardNumberAsync(CancellationToken ct)
    {
        var setting = await _uow.Repository<SystemSetting>().SingleOrDefaultAsync(s => s.SettingKey == LastCardNumberKey && s.OrganizationId == null, ct);
        return long.TryParse(setting?.SettingValue, out var n) ? n : 0;
    }

    private async Task SetLastCardNumberAsync(long value, CancellationToken ct)
    {
        var repo = _uow.Repository<SystemSetting>();
        var setting = await repo.SingleOrDefaultAsync(s => s.SettingKey == LastCardNumberKey && s.OrganizationId == null, ct);
        if (setting is null)
        {
            await repo.AddAsync(new SystemSetting { SettingKey = LastCardNumberKey, SettingValue = value.ToString() }, ct);
        }
        else
        {
            setting.SettingValue = value.ToString();
            setting.ModifiedDate = DateTime.UtcNow;
            repo.Update(setting);
        }
    }

    /// <summary>e.g. C-000123. Sequential per installation.</summary>
    private static string FormatCardNumber(long n) => $"C-{n:D6}";

    private static string ReprintReasonText(ReprintReasonCode? reason, string? note)
    {
        var text = reason?.ToString() ?? "Reprinted";
        return string.IsNullOrWhiteSpace(note) ? text : $"{text}: {note.Trim()}";
    }

    private static string Truncate(string value, int max) => value.Length > max ? value[..max] : value;
}
