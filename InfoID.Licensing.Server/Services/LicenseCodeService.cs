using InfoID.Licensing.Core;
using InfoID.Licensing.Core.Codecs;
using InfoID.Licensing.Server.Data;
using InfoID.Licensing.Server.Entities;
using Microsoft.EntityFrameworkCore;

namespace InfoID.Licensing.Server.Services;

/// <summary>What the License Creation form submits (FR-ADM-4).</summary>
public sealed record CreateLicenseCodesRequest(
    int Quantity,
    LicenseTier Tier,
    LicenseType Type,
    DateOnly? ExpiryDate,
    int? MaxCardsPerPeriod,
    CardLimitPeriod LimitPeriod,
    int MaxActivations,
    string? OrganizationName,
    string? OrganizationType,
    string? Country,
    string? ContactEmail,
    string? ContactPhone,
    string? Notes);

public sealed record LicenseCodeRow(
    long Id,
    string ActivationCode,
    string Tier,
    string LicenseType,
    DateOnly? ExpiryDate,
    int? MaxCardsPerPeriod,
    string Status,
    int ActiveMachines,
    int MaxActivations,
    string? Customer,
    DateTime CreatedDate);

/// <summary>License Creation and search (FR-ADM-4, FR-ADM-7) plus the license actions
/// (FR-ADM-8) that change the database. Actions that must reach a customer's machine
/// (revoke taking effect, extended expiry) apply at that machine's next online
/// re-validation or with a newly generated serial key.</summary>
public sealed class LicenseCodeService
{
    public const int MaxBatchSize = 1000;

    private readonly IDbContextFactory<LicenseDbContext> _dbFactory;
    private readonly AdminContext _admin;
    private readonly AuditService _audit;

    public LicenseCodeService(IDbContextFactory<LicenseDbContext> dbFactory, AdminContext admin, AuditService audit)
    {
        _dbFactory = dbFactory;
        _admin = admin;
        _audit = audit;
    }

    public async Task<IReadOnlyList<string>> CreateAsync(CreateLicenseCodesRequest request)
    {
        _admin.Require(_admin.CanManageLicenses, "create activation codes");

        if (request.Quantity is < 1 or > MaxBatchSize)
            throw new ArgumentException($"Quantity must be between 1 and {MaxBatchSize}.");
        if (request.MaxActivations is < 1 or > 100)
            throw new ArgumentException("Machines per code must be between 1 and 100.");

        var termsProblem = new SerialKeyTerms(request.Tier, request.Type, request.ExpiryDate, request.MaxCardsPerPeriod, request.LimitPeriod).Validate();
        if (termsProblem is not null) throw new ArgumentException(termsProblem);
        if (request.ExpiryDate is { } expiry && expiry <= DateOnly.FromDateTime(DateTime.UtcNow))
            throw new ArgumentException("The expiry date must be in the future.");

        await using var db = await _dbFactory.CreateDbContextAsync();
        db.CurrentUser = _admin.Email;

        var customer = await FindOrCreateCustomerAsync(db, request);
        var batchId = request.Quantity > 1 ? Guid.NewGuid() : (Guid?)null;

        var codes = new List<string>(request.Quantity);
        var existing = new HashSet<string>();
        while (codes.Count < request.Quantity)
        {
            var code = ActivationCode.Generate().Value;
            if (!existing.Add(code) || await db.LicenseCodes.AnyAsync(c => c.ActivationCode == code))
                continue; // 64 random bits: practically never, but never risk a duplicate

            db.LicenseCodes.Add(new LicenseCode
            {
                ActivationCode = code,
                Tier = request.Tier.ToString(),
                LicenseType = request.Type.ToString(),
                ExpiryDate = request.ExpiryDate,
                MaxCardsPerPeriod = request.MaxCardsPerPeriod,
                CardLimitPeriod = request.LimitPeriod.ToString(),
                MaxActivations = request.MaxActivations,
                Status = LicenseCodeStatus.Available,
                BatchId = batchId,
                CustomerRecord = customer,
                CreatedByAdminUserId = _admin.CurrentAdmin?.Id,
                Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            });
            codes.Add(code);
        }

        await db.SaveChangesAsync();
        await _audit.LogAsync("LicenseCode.Created", new
        {
            Count = codes.Count,
            BatchId = batchId,
            Tier = request.Tier.ToString(),
            Type = request.Type.ToString(),
            request.ExpiryDate,
            request.MaxCardsPerPeriod,
            Customer = customer?.OrganizationName,
        });

        return codes.Select(c => ActivationCode.TryParse(c, out var parsed) ? parsed!.ToString() : c).ToList();
    }

    public async Task<IReadOnlyList<LicenseCodeRow>> SearchAsync(string? text, string? status = null, int take = 500)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var query = db.LicenseCodes.AsNoTracking().Where(c => !c.Cancelled);

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(c => c.Status == status);

        if (!string.IsNullOrWhiteSpace(text))
        {
            var term = text.Trim();
            var codeTerm = CrockfordBase32.Normalize(term);
            var like = $"%{term}%";
            query = query.Where(c =>
                (codeTerm.Length >= 4 && c.ActivationCode.Contains(codeTerm)) ||
                (c.CustomerRecord != null && (EF.Functions.ILike(c.CustomerRecord.OrganizationName, like) ||
                                              EF.Functions.ILike(c.CustomerRecord.ContactEmail ?? "", like))) ||
                c.Activations.Any(a => a.SerialKeyIssued != null && EF.Functions.ILike(a.SerialKeyIssued, like)) ||
                c.Activations.Any(a => EF.Functions.ILike(a.MachineFingerprint, like)));
        }

        return await query
            .OrderByDescending(c => c.Id)
            .Take(take)
            .Select(c => new LicenseCodeRow(
                c.Id,
                c.ActivationCode,
                c.Tier,
                c.LicenseType,
                c.ExpiryDate,
                c.MaxCardsPerPeriod,
                c.Status,
                c.Activations.Count(a => a.Status == ActivationStatus.Active && !a.Cancelled),
                c.MaxActivations,
                c.CustomerRecord != null ? c.CustomerRecord.OrganizationName : null,
                c.CreatedDate))
            .ToListAsync();
    }

    public async Task RevokeAsync(long licenseCodeId, string reason)
    {
        _admin.Require(_admin.CanManageLicenses, "revoke licenses");
        await using var db = await _dbFactory.CreateDbContextAsync();
        db.CurrentUser = _admin.Email;

        var code = await db.LicenseCodes.Include(c => c.Activations).FirstAsync(c => c.Id == licenseCodeId);
        code.Status = LicenseCodeStatus.Revoked;
        code.RevokedDate = DateTime.UtcNow;
        foreach (var activation in code.Activations.Where(a => a.Status == ActivationStatus.Active))
        {
            activation.Status = ActivationStatus.Deactivated;
            activation.DeactivatedDate = DateTime.UtcNow;
        }
        await db.SaveChangesAsync();
        await _audit.LogAsync("LicenseCode.Revoked", new { code.ActivationCode, Reason = reason });
    }

    public async Task ExtendAsync(long licenseCodeId, DateOnly newExpiry)
    {
        _admin.Require(_admin.CanManageLicenses, "extend licenses");
        if (newExpiry <= DateOnly.FromDateTime(DateTime.UtcNow))
            throw new ArgumentException("The new expiry date must be in the future.");

        await using var db = await _dbFactory.CreateDbContextAsync();
        db.CurrentUser = _admin.Email;
        var code = await db.LicenseCodes.FirstAsync(c => c.Id == licenseCodeId);
        if (code.LicenseType == nameof(LicenseType.Perpetual))
            throw new InvalidOperationException("Perpetual licenses have no expiry date.");

        var old = code.ExpiryDate;
        code.ExpiryDate = newExpiry;
        await db.SaveChangesAsync();
        await _audit.LogAsync("LicenseCode.Extended", new { code.ActivationCode, OldExpiry = old, NewExpiry = newExpiry });
    }

    private static async Task<CustomerRecord?> FindOrCreateCustomerAsync(LicenseDbContext db, CreateLicenseCodesRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.OrganizationName)) return null;

        var name = request.OrganizationName.Trim();
        var email = string.IsNullOrWhiteSpace(request.ContactEmail) ? null : request.ContactEmail.Trim().ToLowerInvariant();

        var existing = await db.CustomerRecords.FirstOrDefaultAsync(c => !c.Cancelled &&
            (email != null ? c.ContactEmail == email : c.OrganizationName == name));
        if (existing is not null) return existing;

        var customer = new CustomerRecord
        {
            OrganizationName = name,
            OrganizationType = request.OrganizationType,
            Country = request.Country,
            ContactEmail = email,
            ContactPhone = request.ContactPhone,
        };
        db.CustomerRecords.Add(customer);
        return customer;
    }
}
