using InfoID.Licensing.Core;
using InfoID.Licensing.Core.Codecs;
using InfoID.Licensing.Server.Data;
using InfoID.Licensing.Server.Entities;
using Microsoft.EntityFrameworkCore;

namespace InfoID.Licensing.Server.Services;

/// <summary>What the "Generate Serial Key" screen shows after decoding a request code
/// (FR-ADM-6 "auto-decode"), before anything is issued.</summary>
public sealed record SoftCodePreview(
    string ActivationCode,
    string FingerprintHex,
    DateOnly RequestDate,
    long? LicenseCodeId,
    string? CodeStatus,
    string? Tier,
    string? LicenseType,
    DateOnly? ExpiryDate,
    int? MaxCardsPerPeriod,
    string? CardLimitPeriod,
    string? Customer,
    int ActiveMachines,
    int MaxActivations,
    bool IsReactivationOfSameMachine,
    IReadOnlyList<string> Problems);

/// <summary>Optional overrides at generation time (FR-ADM-6 "custom expiry or
/// override tier").</summary>
public sealed record SerialKeyOverrides(LicenseTier? Tier = null, DateOnly? ExpiryDate = null);

public sealed record SerialKeyIssueResult(bool Succeeded, string? SerialKey, string? Error, SerialKeyTerms? Terms)
{
    public static SerialKeyIssueResult Fail(string error) => new(false, null, error, null);
}

/// <summary>
/// Offline activation, vendor side (FR-LIC-6, FR-ADM-6): decode the customer's request
/// code, check the activation code against the database, and issue the 20-character
/// serial key bound to that machine. The future online License API calls
/// <see cref="IssueAsync"/> the same way with mode "Online".
/// </summary>
public sealed class ActivationService
{
    private readonly IDbContextFactory<LicenseDbContext> _dbFactory;
    private readonly AdminContext _admin;
    private readonly AuditService _audit;
    private readonly LicenseSecretStore _secrets;

    public ActivationService(IDbContextFactory<LicenseDbContext> dbFactory, AdminContext admin, AuditService audit, LicenseSecretStore secrets)
    {
        _dbFactory = dbFactory;
        _admin = admin;
        _audit = audit;
        _secrets = secrets;
    }

    public async Task<(SoftCodePreview? Preview, string? Error)> PreviewAsync(string softCodeText)
    {
        if (!SoftCode.TryParse(softCodeText, out var softCode, out var error))
            return (null, error);

        await using var db = await _dbFactory.CreateDbContextAsync();
        var code = await db.LicenseCodes.AsNoTracking()
            .Include(c => c.CustomerRecord)
            .Include(c => c.Activations)
            .FirstOrDefaultAsync(c => c.ActivationCode == softCode!.ActivationCode.Value && !c.Cancelled);

        var fingerprint = softCode!.Fingerprint.Hex;
        var problems = new List<string>();

        if (code is null)
        {
            problems.Add("This activation code does not exist in the license database.");
            return (new SoftCodePreview(softCode.ActivationCode.ToString(), fingerprint, softCode.RequestDate,
                null, null, null, null, null, null, null, null, 0, 0, false, problems), null);
        }

        var active = code.Activations.Where(a => a.Status == ActivationStatus.Active && !a.Cancelled).ToList();
        var sameMachine = active.Any(a => a.MachineFingerprint == fingerprint);

        if (code.Status == LicenseCodeStatus.Revoked)
            problems.Add("This activation code has been revoked.");
        if (!sameMachine && active.Count >= code.MaxActivations)
            problems.Add($"All {code.MaxActivations} activation slot(s) are in use. The customer must deactivate the old machine first, or use Transfer.");
        if (code.ExpiryDate is { } expiry && expiry < DateOnly.FromDateTime(DateTime.UtcNow))
            problems.Add($"This license expired on {expiry:d}. Extend it before issuing a key.");
        if (await IsBlacklistedAsync(db, fingerprint, code.ActivationCode))
            problems.Add("This machine or activation code is blacklisted.");
        if (softCode.RequestDate < DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-30))
            problems.Add($"This request code is from {softCode.RequestDate:d}, more than 30 days ago. Ask the customer for a new one if in doubt.");

        return (new SoftCodePreview(
            softCode.ActivationCode.ToString(), fingerprint, softCode.RequestDate,
            code.Id, code.Status, code.Tier, code.LicenseType, code.ExpiryDate, code.MaxCardsPerPeriod, code.CardLimitPeriod,
            code.CustomerRecord?.OrganizationName, active.Count, code.MaxActivations, sameMachine, problems), null);
    }

    public async Task<SerialKeyIssueResult> IssueAsync(string softCodeText, SerialKeyOverrides? overrides = null, string mode = "Offline", string? appVersion = null)
    {
        _admin.Require(_admin.CanGenerateSerialKeys, "generate serial keys");

        var (preview, error) = await PreviewAsync(softCodeText);
        if (preview is null) return SerialKeyIssueResult.Fail(error ?? "Invalid request code.");

        // The 30-day age warning is advisory; every other problem blocks issuing.
        var blocking = preview.Problems.Where(p => !p.Contains("more than 30 days ago", StringComparison.Ordinal)).ToList();
        if (blocking.Count > 0) return SerialKeyIssueResult.Fail(string.Join(" ", blocking));

        if (overrides is { } o && (o.Tier is not null || o.ExpiryDate is not null))
            _admin.Require(_admin.CanManageLicenses, "override tier or expiry");

        SoftCode.TryParse(softCodeText, out var softCode, out _);

        await using var db = await _dbFactory.CreateDbContextAsync();
        db.CurrentUser = _admin.Email;
        var code = await db.LicenseCodes.Include(c => c.Activations).FirstAsync(c => c.Id == preview.LicenseCodeId);

        var terms = new SerialKeyTerms(
            overrides?.Tier ?? Enum.Parse<LicenseTier>(code.Tier),
            Enum.Parse<LicenseType>(code.LicenseType),
            overrides?.ExpiryDate ?? code.ExpiryDate,
            code.MaxCardsPerPeriod,
            Enum.Parse<CardLimitPeriod>(code.CardLimitPeriod));

        var termsProblem = terms.Validate();
        if (termsProblem is not null) return SerialKeyIssueResult.Fail(termsProblem);

        string serialKey;
        try
        {
            serialKey = SerialKey.Generate(terms, softCode!.ActivationCode, softCode.Fingerprint, _secrets.Current);
        }
        catch (InvalidOperationException ex)
        {
            return SerialKeyIssueResult.Fail(ex.Message);
        }

        // Re-issuing for a machine that already holds this code (reinstall, new key
        // after an extension) replaces its active record instead of using a new slot.
        foreach (var previous in code.Activations.Where(a => a.Status == ActivationStatus.Active && a.MachineFingerprint == preview.FingerprintHex))
        {
            previous.Status = ActivationStatus.Deactivated;
            previous.DeactivatedDate = DateTime.UtcNow;
        }

        db.ActivationRecords.Add(new ActivationRecord
        {
            LicenseCodeId = code.Id,
            MachineFingerprint = preview.FingerprintHex,
            SerialKeyIssued = serialKey,
            SoftCode = CrockfordBaseNormalize(softCodeText),
            ActivationMode = mode,
            Status = ActivationStatus.Active,
            ActivationDate = DateTime.UtcNow,
            IssuedTier = terms.Tier.ToString(),
            IssuedExpiryDate = terms.ExpiryDate,
            AppVersion = appVersion,
            GeneratedByAdminUserId = mode == "Offline" ? _admin.CurrentAdmin?.Id : null,
        });

        code.IsUsed = true;
        code.Status = LicenseCodeStatus.Active;
        await db.SaveChangesAsync();

        await _audit.LogAsync("SerialKey.Issued", new
        {
            ActivationCode = preview.ActivationCode,
            Fingerprint = preview.FingerprintHex,
            Mode = mode,
            Tier = terms.Tier.ToString(),
            terms.ExpiryDate,
            Overridden = overrides is not null && (overrides.Tier is not null || overrides.ExpiryDate is not null),
        });

        return new SerialKeyIssueResult(true, serialKey, null, terms);
    }

    private static string CrockfordBaseNormalize(string text) => CrockfordBase32.Normalize(text);

    private static Task<bool> IsBlacklistedAsync(LicenseDbContext db, string fingerprint, string activationCode) =>
        db.BlacklistEntries.AnyAsync(b => !b.Cancelled &&
            ((b.EntryType == "Fingerprint" && b.Value == fingerprint) ||
             (b.EntryType == "ActivationCode" && b.Value == activationCode)));
}
