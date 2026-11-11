using InfoID.Application.Dtos.Encoding;
using InfoID.Application.Interfaces;
using InfoID.Domain.Common.Enums;
using InfoID.Domain.Common.Interfaces;
using InfoID.Domain.Entities.CardholderModule;
using InfoID.Domain.Entities.IssuanceModule;
using InfoID.Domain.Entities.RfidModule;

namespace InfoID.Application.Services;

/// <summary>
/// Uses the same records as Print history: Cardholder.ExternalRefId = Person ID, one Card per
/// person per design, the shared C-000123 sequence. A tag UID belongs to at most one live card
/// (Draft / Issued / Active / Suspended / Expired); Replaced and Revoked cards free their tag.
/// </summary>
public sealed class NfcEncodingService : INfcEncodingService
{
    private readonly IUnitOfWork _uow;
    private readonly IAuditTrail? _audit;

    public NfcEncodingService(IUnitOfWork uow, IAuditTrail? audit = null)
    {
        _uow = uow;
        _audit = audit;
    }

    public async Task<NfcTagOwnerDto?> FindTagOwnerAsync(string uid, CancellationToken ct = default)
    {
        var normalized = NfcUid.Normalize(uid);
        if (normalized.Length == 0) return null;

        var cards = await _uow.Repository<Card>().FindAsync(c =>
            c.RFIDUid == normalized && c.Status != CardStatus.Replaced && c.Status != CardStatus.Revoked, ct);
        var card = cards.OrderByDescending(c => c.ModifiedDate ?? c.CreatedDate).FirstOrDefault();
        if (card is null) return null;

        var holder = await _uow.Repository<Cardholder>().GetByIdAsync(card.CardholderId, ct);
        return new NfcTagOwnerDto
        {
            CardId = card.Id,
            CardNumber = card.CardNumber,
            TemplateId = card.TemplateId,
            PersonKey = holder?.ExternalRefId,
            FullName = holder?.FullName ?? string.Empty,
            Status = card.Status,
        };
    }

    public async Task<NfcTagAssignmentResult> AssignTagAsync(NfcTagAssignment assignment, CancellationToken ct = default)
    {
        var uid = NfcUid.Normalize(assignment.Uid);
        var key = assignment.PersonKey?.Trim() ?? string.Empty;
        if (uid.Length == 0) throw new ArgumentException("The tag has no UID.", nameof(assignment));
        if (key.Length == 0) throw new ArgumentException("The record has no Person ID.", nameof(assignment));

        var owner = await FindTagOwnerAsync(uid, ct);
        if (owner is not null && !(owner.TemplateId == assignment.TemplateId &&
                                   string.Equals(owner.PersonKey, key, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"This tag is already linked to card {owner.CardNumber}.");

        var now = DateTime.UtcNow;
        var (card, holder) = await FindCardForPersonAsync(assignment.TemplateId, key, ct);
        var result = new NfcTagAssignmentResult();

        if (card is null)
        {
            var organizationId = await IssuanceStore.EnsureOrganizationAsync(_uow, ct);
            var userId = await IssuanceStore.EnsureOperatorAsync(_uow, organizationId, ct);
            var name = IssuanceStore.Truncate(string.IsNullOrWhiteSpace(assignment.DisplayName) ? key : assignment.DisplayName.Trim(), 200);

            if (holder is null)
            {
                holder = new Cardholder
                {
                    OrganizationId = organizationId,
                    FullName = name,
                    ExternalRefId = IssuanceStore.Truncate(key, 100),
                    IdNumber = IssuanceStore.Truncate(key, 100),
                };
                await _uow.Repository<Cardholder>().AddAsync(holder, ct);
            }

            var number = await IssuanceStore.GetLastCardNumberAsync(_uow, ct) + 1;
            card = new Card
            {
                Cardholder = holder,
                TemplateId = assignment.TemplateId,
                CardNumber = IssuanceStore.FormatCardNumber(number),
                Status = CardStatus.Draft,
                RFIDUid = uid,
            };
            await _uow.Repository<Card>().AddAsync(card, ct);
            await _uow.Repository<CardStatusHistory>().AddAsync(new CardStatusHistory
            {
                Card = card,
                FromStatus = null,
                ToStatus = CardStatus.Draft,
                ChangedDate = now,
                Reason = "NFC tag encoded before printing",
                ChangedByUserId = userId,
            }, ct);
            await IssuanceStore.SetLastCardNumberAsync(_uow, number, ct);
            result.CreatedCard = true;
        }
        else
        {
            result.PreviousUid = string.Equals(card.RFIDUid, uid, StringComparison.OrdinalIgnoreCase) ? null : card.RFIDUid;
            card.RFIDUid = uid;
            card.ModifiedDate = now;
            _uow.Repository<Card>().Update(card);
        }

        await _uow.Repository<RfidEncodeLog>().AddAsync(new RfidEncodeLog
        {
            Card = card,
            Operation = RfidOperation.Encode,
            Result = RfidResult.Success,
            EncodedDate = now,
        }, ct);
        await _uow.SaveChangesAsync(ct);

        result.CardId = card.Id;
        result.CardNumber = card.CardNumber;
        return result;
    }

    public async Task RecordFailureAsync(long? templateId, string? personKey, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(personKey)) return;
        var (card, _) = await FindCardForPersonAsync(templateId, personKey.Trim(), ct);
        if (card is null) return; // RfidEncodeLog needs a card; the batch audit still counts it

        await _uow.Repository<RfidEncodeLog>().AddAsync(new RfidEncodeLog
        {
            CardId = card.Id,
            Operation = RfidOperation.Encode,
            Result = RfidResult.Failed,
            EncodedDate = DateTime.UtcNow,
            FailureReason = IssuanceStore.Truncate(reason, 300),
        }, ct);
        await _uow.SaveChangesAsync(ct);
    }

    public Task RecordBatchAsync(NfcEncodingBatchSummary s, CancellationToken ct = default)
    {
        if (_audit is null) return Task.CompletedTask;
        var design = string.IsNullOrWhiteSpace(s.DesignName) ? "Untitled design" : s.DesignName.Trim();
        var action = s.Stopped ? "NfcWriteStopped" : s.Written == 0 && s.Failed > 0 ? "NfcWriteFailed" : "NfcWritten";
        var description = $"Wrote {s.Written} NFC tag{(s.Written == 1 ? "" : "s")} for '{design}'" +
                          (s.Skipped > 0 ? $", {s.Skipped} skipped" : "") +
                          (s.Failed > 0 ? $", {s.Failed} not written" : "") +
                          (s.Stopped ? " (stopped early)" : "");

        return _audit.RecordAsync(new AuditEntry(AuditModules.Encoding, action, description)
        {
            EntityName = s.TemplateId is null ? null : "Template",
            EntityId = s.TemplateId,
            After = new { s.Records, s.Written, s.Skipped, s.Failed, s.UidsSavedToData, s.Stopped },
        }, ct);
    }

    /// <summary>The person's card for this design: Issued/Active first, then Draft. Also returns an
    /// existing Cardholder with this Person ID so a new Draft card doesn't duplicate the person.</summary>
    private async Task<(Card? Card, Cardholder? Holder)> FindCardForPersonAsync(long? templateId, string key, CancellationToken ct)
    {
        var holders = await _uow.Repository<Cardholder>().FindAsync(h => h.ExternalRefId == key, ct);
        if (holders.Count == 0) return (null, null);

        var holderIds = holders.Select(h => h.Id).ToList();
        var cards = await _uow.Repository<Card>().FindAsync(c =>
            c.TemplateId == templateId && holderIds.Contains(c.CardholderId) &&
            (c.Status == CardStatus.Issued || c.Status == CardStatus.Active || c.Status == CardStatus.Draft), ct);

        var card = cards
            .OrderBy(c => c.Status == CardStatus.Draft ? 1 : 0)
            .ThenByDescending(c => c.IssuedDate ?? c.CreatedDate)
            .FirstOrDefault();
        var holder = card is not null
            ? holders.First(h => h.Id == card.CardholderId)
            : holders.OrderByDescending(h => h.CreatedDate).First();
        return (card, holder);
    }
}
