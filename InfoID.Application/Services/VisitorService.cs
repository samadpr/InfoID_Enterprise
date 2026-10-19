using System.Globalization;
using System.Text.Json;
using InfoID.Application.Dtos.VisitorModule;
using InfoID.Application.Interfaces;
using InfoID.Domain.Common.Enums;
using InfoID.Domain.Common.Interfaces;
using InfoID.Domain.Entities.CardholderModule;
using InfoID.Domain.Entities.IssuanceModule;
using InfoID.Domain.Entities.SystemModule;
using InfoID.Domain.Entities.VisitorModule;

namespace InfoID.Application.Services;

/// <summary>
/// Visitor registrations (see <see cref="IVisitorService"/>).
///
/// Visitor IDs (V-000001 …) come from a running counter in SystemSetting, so an ID is
/// never reused even after visitors are deleted -- Print history keys cards by this ID,
/// and a reused ID would make a new visitor look "already printed".
///
/// Audit entries carry visitor IDs and counts only, never names, phones or emails.
/// </summary>
public sealed class VisitorService : IVisitorService
{
    private const string LastNumberKey = "Visitors.LastNumber";
    private const string BadgeTemplateKey = "Visitors.BadgeTemplateId";
    private const string CodePrefix = "V-";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    private readonly IUnitOfWork _uow;
    private readonly IAuditTrail? _audit;

    public VisitorService(IUnitOfWork uow, IAuditTrail? audit = null)
    {
        _uow = uow;
        _audit = audit;
    }

    // ------------------------------------------------------------------ queries ----

    public async Task<IReadOnlyList<VisitorDto>> GetVisitorsAsync(VisitorFilter filter, CancellationToken ct = default)
    {
        var all = await _uow.Repository<Visitor>().GetAllAsync(ct);
        IEnumerable<Visitor> query = all;

        var today = filter.Today;
        query = filter.Period switch
        {
            VisitorPeriod.Today => query.Where(v => DateOf(v) == today),
            VisitorPeriod.Upcoming => query.Where(v => DateOf(v) >= today),
            VisitorPeriod.Last7Days => query.Where(v => DateOf(v) >= today.AddDays(-6) && DateOf(v) <= today),
            VisitorPeriod.Last30Days => query.Where(v => DateOf(v) >= today.AddDays(-29) && DateOf(v) <= today),
            _ => query,
        };

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(v =>
                Contains(v.VisitorCode, term) || Contains(v.FullName, term) || Contains(v.Phone, term) ||
                Contains(v.Email, term) || Contains(v.Company, term) || Contains(v.PersonToMeet, term));
        }

        var list = query.ToList();
        var dtos = await ToDtosAsync(list, ct);

        IEnumerable<VisitorDto> result = filter.PrintStatus switch
        {
            VisitorPrintFilter.NotPrinted => dtos.Where(d => !d.IsPrinted),
            VisitorPrintFilter.Printed => dtos.Where(d => d.IsPrinted),
            _ => dtos,
        };

        // Newest registrations first; same visit date -> by visitor ID.
        return result
            .OrderByDescending(d => d.VisitDate ?? DateOnly.FromDateTime(d.CreatedUtc.ToLocalTime()))
            .ThenByDescending(d => d.VisitorCode, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<IReadOnlyList<VisitorDto>> GetByIdsAsync(IReadOnlyCollection<long> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0) return Array.Empty<VisitorDto>();
        var idList = ids.Distinct().ToList();
        var visitors = await _uow.Repository<Visitor>().FindAsync(v => idList.Contains(v.Id), ct);
        var dtos = await ToDtosAsync(visitors.ToList(), ct);
        // Keep the caller's order (e.g. the order selected in the list).
        var order = idList.Select((id, index) => (id, index)).ToDictionary(x => x.id, x => x.index);
        return dtos.OrderBy(d => order[d.Id]).ToList();
    }

    public async Task<IReadOnlySet<string>> FindImportedSourceKeysAsync(IReadOnlyCollection<string> sourceKeys, CancellationToken ct = default)
    {
        var keys = sourceKeys.Where(k => !string.IsNullOrWhiteSpace(k)).Distinct(StringComparer.Ordinal).ToList();
        if (keys.Count == 0) return new HashSet<string>(StringComparer.Ordinal);
        var found = await _uow.Repository<Visitor>().FindAsync(v => v.SourceKey != null && keys.Contains(v.SourceKey), ct);
        return found.Select(v => v.SourceKey!).ToHashSet(StringComparer.Ordinal);
    }

    // ------------------------------------------------------------------ changes ----

    public async Task<VisitorDto> AddAsync(VisitorInput input, CancellationToken ct = default)
    {
        VisitorValidation.Normalize(input);
        ThrowIfInvalid(input);

        var next = await NextNumberAsync(ct);
        var visitor = NewVisitor(input, FormatCode(next), VisitorSource.Manual, "Entered in InfoID", null, null);
        await _uow.Repository<Visitor>().AddAsync(visitor, ct);
        await SetLastNumberAsync(next, ct);
        await _uow.SaveChangesAsync(ct);

        await AuditAsync("VisitorAdded", $"Visitor {visitor.VisitorCode} added", visitor.Id,
            after: new { visitor.VisitorCode, Source = visitor.Source.ToString() }, ct: ct);
        return (await ToDtosAsync(new List<Visitor> { visitor }, ct))[0];
    }

    public async Task<(VisitorDto Visitor, string? ReplacedPhotoPath)> UpdateAsync(long id, VisitorInput input, CancellationToken ct = default)
    {
        VisitorValidation.Normalize(input);
        ThrowIfInvalid(input);

        var repo = _uow.Repository<Visitor>();
        var visitor = await repo.GetByIdAsync(id, ct)
                      ?? throw new InvalidOperationException("This visitor no longer exists. Refresh the list.");

        var changed = ChangedFields(visitor, input);
        var oldPhoto = visitor.PhotoPath;
        Apply(visitor, input);
        visitor.ModifiedDate = DateTime.UtcNow;
        repo.Update(visitor);
        await _uow.SaveChangesAsync(ct);

        if (changed.Count > 0)
        {
            // Field names only -- the values are personal data.
            await AuditAsync("VisitorEdited", $"Visitor {visitor.VisitorCode} edited ({string.Join(", ", changed)})", visitor.Id,
                after: new { visitor.VisitorCode, ChangedFields = changed }, ct: ct);
        }

        var replaced = !string.IsNullOrEmpty(oldPhoto) && !string.Equals(oldPhoto, visitor.PhotoPath, StringComparison.OrdinalIgnoreCase)
            ? oldPhoto
            : null;
        return ((await ToDtosAsync(new List<Visitor> { visitor }, ct))[0], replaced);
    }

    public async Task<IReadOnlyList<string>> DeleteAsync(IReadOnlyCollection<long> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0) return Array.Empty<string>();
        var idList = ids.Distinct().ToList();
        var repo = _uow.Repository<Visitor>();
        var visitors = await repo.FindAsync(v => idList.Contains(v.Id), ct);
        if (visitors.Count == 0) return Array.Empty<string>();

        var photos = visitors.Where(v => !string.IsNullOrEmpty(v.PhotoPath)).Select(v => v.PhotoPath!).ToList();
        var codes = visitors.Select(v => v.VisitorCode).OrderBy(c => c, StringComparer.Ordinal).ToList();
        foreach (var visitor in visitors) repo.HardDelete(visitor);
        await _uow.SaveChangesAsync(ct);

        await AuditAsync("VisitorsDeleted",
            codes.Count == 1 ? $"Visitor {codes[0]} deleted" : $"{codes.Count} visitors deleted",
            codes.Count == 1 ? visitors[0].Id : null,
            before: new { Visitors = codes }, ct: ct);
        return photos;
    }

    public async Task<VisitorImportResult> ImportAsync(VisitorImportRequest request, CancellationToken ct = default)
    {
        var result = new VisitorImportResult { BatchKey = Guid.NewGuid() };
        var today = DateOnly.FromDateTime(DateTime.Today);

        var keys = request.Items.Select(i => i.SourceKey).Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k!).ToList();
        var imported = await FindImportedSourceKeysAsync(keys, ct);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        var repo = _uow.Repository<Visitor>();
        var number = await NextNumberAsync(ct) - 1;
        var created = new List<Visitor>();

        foreach (var item in request.Items)
        {
            VisitorValidation.Normalize(item);
            var key = string.IsNullOrWhiteSpace(item.SourceKey) ? null : item.SourceKey.Trim();
            if (key is not null && (imported.Contains(key) || !seen.Add(key)))
            {
                result.SkippedDuplicates++;
                continue;
            }
            if (VisitorValidation.HasErrors(VisitorValidation.Validate(item, today)))
            {
                result.SkippedInvalid++;
                continue;
            }

            number++;
            item.VisitDate ??= today;
            var visitor = NewVisitor(item, FormatCode(number), request.Source, Truncate(request.SourceName, 260), key, result.BatchKey);
            await repo.AddAsync(visitor, ct);
            created.Add(visitor);
        }

        if (created.Count > 0)
        {
            await SetLastNumberAsync(number, ct);
            await _uow.SaveChangesAsync(ct);
        }

        result.Imported = created.Count;
        result.Created = (await ToDtosAsync(created, ct)).ToList();

        if (created.Count > 0 || result.SkippedDuplicates > 0)
        {
            var range = created.Count == 0 ? "" : created.Count == 1 ? $" ({created[0].VisitorCode})" : $" ({created[0].VisitorCode} – {created[^1].VisitorCode})";
            await AuditAsync("VisitorsImported",
                $"{created.Count} visitor{(created.Count == 1 ? "" : "s")} imported from {SourceLabel(request)}{range}" +
                (result.SkippedDuplicates > 0 ? $", {result.SkippedDuplicates} already imported" : ""),
                null,
                after: new
                {
                    Source = request.Source.ToString(),
                    Imported = created.Count,
                    result.SkippedDuplicates,
                    result.SkippedInvalid,
                    Batch = result.BatchKey,
                    First = created.FirstOrDefault()?.VisitorCode,
                    Last = created.LastOrDefault()?.VisitorCode,
                }, ct: ct);
        }
        return result;
    }

    // ----------------------------------------------------------------- settings ----

    public async Task<VisitorSettingsDto> GetSettingsAsync(CancellationToken ct = default)
    {
        var value = await GetSettingAsync(BadgeTemplateKey, ct);
        return new VisitorSettingsDto
        {
            BadgeTemplateId = long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : null,
        };
    }

    public async Task SaveSettingsAsync(VisitorSettingsDto settings, CancellationToken ct = default)
    {
        var before = await GetSettingsAsync(ct);
        if (before.BadgeTemplateId == settings.BadgeTemplateId) return;

        await SetSettingAsync(BadgeTemplateKey, settings.BadgeTemplateId?.ToString(CultureInfo.InvariantCulture), ct);
        await _uow.SaveChangesAsync(ct);
        await AuditAsync("BadgeDesignChanged", "Visitor badge design changed", null,
            before: new { before.BadgeTemplateId }, after: new { settings.BadgeTemplateId }, ct: ct);
    }

    // ------------------------------------------------------------------ helpers ----

    private static DateOnly DateOf(Visitor v) => v.VisitDate ?? DateOnly.FromDateTime(v.CreatedDate.ToLocalTime());

    private static bool Contains(string? value, string term) =>
        value is not null && value.Contains(term, StringComparison.OrdinalIgnoreCase);

    private static string FormatCode(long number) => CodePrefix + number.ToString("D6", CultureInfo.InvariantCulture);

    private static void ThrowIfInvalid(VisitorInput input)
    {
        var errors = VisitorValidation.Validate(input, DateOnly.FromDateTime(DateTime.Today))
            .Where(i => i.Severity == VisitorIssueSeverity.Error).ToList();
        if (errors.Count > 0)
            throw new InvalidOperationException(string.Join(" ", errors.Select(e => e.Message)));
    }

    private static Visitor NewVisitor(VisitorInput input, string code, VisitorSource source, string? sourceName, string? sourceKey, Guid? batch)
    {
        var visitor = new Visitor
        {
            VisitorCode = code,
            Source = source,
            SourceName = sourceName,
            SourceKey = Truncate(sourceKey, 300),
            ImportBatchKey = batch,
        };
        Apply(visitor, input);
        visitor.VisitDate ??= DateOnly.FromDateTime(DateTime.Today);
        return visitor;
    }

    private static void Apply(Visitor visitor, VisitorInput input)
    {
        visitor.FullName = input.FullName;
        visitor.Phone = input.Phone;
        visitor.Email = input.Email;
        visitor.Company = input.Company;
        visitor.PersonToMeet = input.PersonToMeet;
        visitor.Purpose = input.Purpose;
        visitor.VisitDate = input.VisitDate ?? visitor.VisitDate;
        visitor.PhotoPath = input.PhotoPath;
        visitor.ExtraFieldsJson = input.ExtraFields.Count == 0 ? null : JsonSerializer.Serialize(input.ExtraFields, Json);
    }

    private static List<string> ChangedFields(Visitor v, VisitorInput i)
    {
        var changed = new List<string>();
        void Check(string field, string? a, string? b)
        {
            if (!string.Equals(a ?? "", b ?? "", StringComparison.Ordinal)) changed.Add(field);
        }
        Check(VisitorValidation.FieldName, v.FullName, i.FullName);
        Check(VisitorValidation.FieldPhone, v.Phone, i.Phone);
        Check(VisitorValidation.FieldEmail, v.Email, i.Email);
        Check(VisitorValidation.FieldCompany, v.Company, i.Company);
        Check(VisitorValidation.FieldPersonToMeet, v.PersonToMeet, i.PersonToMeet);
        Check(VisitorValidation.FieldPurpose, v.Purpose, i.Purpose);
        if (i.VisitDate is not null && v.VisitDate != i.VisitDate) changed.Add(VisitorValidation.FieldVisitDate);
        Check(VisitorValidation.FieldPhoto, v.PhotoPath, i.PhotoPath);
        var extras = i.ExtraFields.Count == 0 ? null : JsonSerializer.Serialize(i.ExtraFields, Json);
        Check("Other fields", v.ExtraFieldsJson, extras);
        return changed;
    }

    private async Task<List<VisitorDto>> ToDtosAsync(List<Visitor> visitors, CancellationToken ct)
    {
        var printInfo = await GetPrintInfoAsync(visitors.Select(v => v.VisitorCode).ToList(), ct);
        return visitors.Select(v =>
        {
            printInfo.TryGetValue(v.VisitorCode, out var info);
            return new VisitorDto
            {
                Id = v.Id,
                VisitorCode = v.VisitorCode,
                FullName = v.FullName,
                Phone = v.Phone,
                Email = v.Email,
                Company = v.Company,
                PersonToMeet = v.PersonToMeet,
                Purpose = v.Purpose,
                VisitDate = v.VisitDate,
                PhotoPath = v.PhotoPath,
                ExtraFields = ReadExtras(v.ExtraFieldsJson),
                Source = v.Source,
                SourceName = v.SourceName,
                CreatedUtc = DateTime.SpecifyKind(v.CreatedDate, DateTimeKind.Utc),
                PrintCount = info.Count,
                LastPrintedUtc = info.Last,
            };
        }).ToList();
    }

    /// <summary>Cards issued per visitor ID, from Print history (Cardholder.ExternalRefId is
    /// the Person ID column of the print -- "VisitorId" for visitor badges).</summary>
    private async Task<Dictionary<string, (int Count, DateTime? Last)>> GetPrintInfoAsync(List<string> codes, CancellationToken ct)
    {
        var result = new Dictionary<string, (int, DateTime?)>(StringComparer.OrdinalIgnoreCase);
        if (codes.Count == 0) return result;

        var holders = await _uow.Repository<Cardholder>().FindAsync(h => h.ExternalRefId != null && codes.Contains(h.ExternalRefId), ct);
        if (holders.Count == 0) return result;

        var holderIds = holders.Select(h => h.Id).ToList();
        var cards = await _uow.Repository<Card>().FindAsync(c => holderIds.Contains(c.CardholderId), ct);
        // A visitor printed with two different designs has two Cardholder rows (Print history
        // keys people per design), so group by visitor ID, not by cardholder.
        var codeOfHolder = holders.ToDictionary(h => h.Id, h => h.ExternalRefId!);
        foreach (var group in cards.GroupBy(c => codeOfHolder[c.CardholderId], StringComparer.OrdinalIgnoreCase))
        {
            var last = group.Max(c => c.IssuedDate ?? c.CreatedDate);
            result[group.Key] = (group.Count(), DateTime.SpecifyKind(last, DateTimeKind.Utc));
        }
        return result;
    }

    private static Dictionary<string, string> ReadExtras(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new(StringComparer.OrdinalIgnoreCase);
        try
        {
            var values = JsonSerializer.Deserialize<Dictionary<string, string>>(json, Json);
            return values is null
                ? new(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>Next free number: one past the counter, and never at or below the highest
    /// visitor ID present (e.g. after restoring an older backup over newer settings).</summary>
    private async Task<long> NextNumberAsync(CancellationToken ct)
    {
        var counter = long.TryParse(await GetSettingAsync(LastNumberKey, ct), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;
        var codes = (await _uow.Repository<Visitor>().GetAllAsync(ct)).Select(v => v.VisitorCode);
        var highest = codes
            .Select(c => c.StartsWith(CodePrefix, StringComparison.Ordinal) && long.TryParse(c[CodePrefix.Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var x) ? x : 0)
            .DefaultIfEmpty(0)
            .Max();
        return Math.Max(counter, highest) + 1;
    }

    private Task SetLastNumberAsync(long value, CancellationToken ct) =>
        SetSettingAsync(LastNumberKey, value.ToString(CultureInfo.InvariantCulture), ct);

    private async Task<string?> GetSettingAsync(string key, CancellationToken ct) =>
        (await _uow.Repository<SystemSetting>().SingleOrDefaultAsync(s => s.SettingKey == key && s.OrganizationId == null, ct))?.SettingValue;

    private async Task SetSettingAsync(string key, string? value, CancellationToken ct)
    {
        var repo = _uow.Repository<SystemSetting>();
        var setting = await repo.SingleOrDefaultAsync(s => s.SettingKey == key && s.OrganizationId == null, ct);
        if (setting is null)
        {
            await repo.AddAsync(new SystemSetting { SettingKey = key, SettingValue = value }, ct);
        }
        else
        {
            setting.SettingValue = value;
            setting.ModifiedDate = DateTime.UtcNow;
            repo.Update(setting);
        }
    }

    private static string SourceLabel(VisitorImportRequest request) =>
        string.IsNullOrWhiteSpace(request.SourceName)
            ? request.Source == VisitorSource.GoogleForm ? "Google Form" : "a file"
            : request.SourceName.Trim();

    private static string? Truncate(string? value, int max) =>
        value is null ? null : value.Length > max ? value[..max] : value;

    private Task AuditAsync(string action, string description, long? entityId, object? before = null, object? after = null, CancellationToken ct = default) =>
        _audit is null
            ? Task.CompletedTask
            : _audit.RecordAsync(new AuditEntry(AuditModules.Visitors, action, description)
            {
                EntityName = "Visitor",
                EntityId = entityId,
                Before = before,
                After = after,
            }, ct);
}
