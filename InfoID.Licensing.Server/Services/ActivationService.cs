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
    bool AllowOfflineActivation,
    IReadOnlyList<string> Problems);

/// <summary>Optional overrides at generation time (FR-ADM-6 "custom expiry or
/// override tier").</summary>
public sealed record SerialKeyOverrides(LicenseTier? Tier = null, DateOnly? ExpiryDate = null);

public sealed record SerialKeyIssueResult(bool Succeeded, string? SerialKey, string? Error, SerialKeyTerms? Terms)
{
    public static SerialKeyIssueResult Fail(string error) => new(false, null, error, null);
}

/// <summary>Result of an online activation or check-in: a signed license token.</summary>
public sealed record OnlineLicenseResult(bool Succeeded, string? License, string? Error, string Status)
{
    public static OnlineLicenseResult Fail(string error, string status = "Refused") => new(false, null, error, status);
}

/// <summary>
/// All activation paths, one set of rules (FR-LIC-5/6/10/13, FR-ADM-6):
///   - Staff, offline:  request code -> 20-char serial key (License Manager).
///   - Phone, offline:  request code from the QR page -> 20-char serial key, no staff,
///                      only for codes with AllowOfflineActivation.
///   - Online:          activation code + fingerprint -> ECDSA-signed license token.
///   - Check-in:        online machines re-validate; renewals arrive, revokes apply.
///   - Deactivate:      frees the machine slot (transfer to a new PC).
/// Every path checks the same things: code exists, not revoked, not expired, not
/// blacklisted, free machine slot (re-activating the SAME machine never uses a new one).
/// </summary>
public sealed class ActivationService
{
    private static readonly InfoID.Shared.Logging.AppLogger Log = InfoID.Shared.Logging.AppLog.For<ActivationService>();

    /// <summary>How long an online license keeps working without a successful check-in.
    /// InfoID tries to check in far more often (see the desktop client).</summary>
    public static readonly TimeSpan CheckInWindow = TimeSpan.FromDays(60);

    private readonly IDbContextFactory<LicenseDbContext> _dbFactory;
    private readonly AdminContext _admin;
    private readonly AuditService _audit;
    private readonly LicenseSecretStore _secrets;
    private readonly LicenseSigningKey? _signingKey;

    public ActivationService(IDbContextFactory<LicenseDbContext> dbFactory, AdminContext admin, AuditService audit,
        LicenseSecretStore secrets, LicenseSigningKey? signingKey = null)
    {
        _dbFactory = dbFactory;
        _admin = admin;
        _audit = audit;
        _secrets = secrets;
        _signingKey = signingKey;
    }

    // ================================================================ offline (request code) ====

    public async Task<(SoftCodePreview? Preview, string? Error)> PreviewAsync(string softCodeText)
    {
        if (!SoftCode.TryParse(softCodeText, out var softCode, out var error))
            return (null, error);

        await using var db = await _dbFactory.CreateDbContextAsync();
        return (await BuildPreviewAsync(db, softCode!.ActivationCode, softCode.Fingerprint, softCode.RequestDate), null);
    }

    /// <summary>Staff-issued key from License Manager (always allowed, staff decide).</summary>
    public async Task<SerialKeyIssueResult> IssueAsync(string softCodeText, SerialKeyOverrides? overrides = null, string mode = "Offline", string? appVersion = null)
    {
        _admin.Require(_admin.CanGenerateSerialKeys, "generate serial keys");
        if (overrides is { } o && (o.Tier is not null || o.ExpiryDate is not null))
            _admin.Require(_admin.CanManageLicenses, "override tier or expiry");

        return await IssueSerialKeyAsync(softCodeText, overrides, mode, appVersion, requireOfflineAllowed: false);
    }

    /// <summary>Self-service phone/QR page: same checks, no staff, no overrides, and
    /// only for codes where staff enabled offline activation.</summary>
    public Task<SerialKeyIssueResult> IssueForPhoneAsync(string softCodeText) =>
        IssueSerialKeyAsync(softCodeText, null, "Phone", null, requireOfflineAllowed: true);

    private async Task<SerialKeyIssueResult> IssueSerialKeyAsync(
        string softCodeText, SerialKeyOverrides? overrides, string mode, string? appVersion, bool requireOfflineAllowed)
    {
        var (preview, error) = await PreviewAsync(softCodeText);
        if (preview is null) return SerialKeyIssueResult.Fail(error ?? "Invalid request code.");

        // The 30-day age warning is advisory; every other problem blocks issuing.
        var blocking = preview.Problems.Where(p => !p.Contains("more than 30 days ago", StringComparison.Ordinal)).ToList();
        if (blocking.Count > 0) return SerialKeyIssueResult.Fail(string.Join(" ", blocking));

        if (requireOfflineAllowed && !preview.AllowOfflineActivation)
            return SerialKeyIssueResult.Fail("This license can only be activated online. Connect the computer to the internet and click Activate, or contact your vendor.");

        SoftCode.TryParse(softCodeText, out var softCode, out _);

        await using var db = await _dbFactory.CreateDbContextAsync();
        db.CurrentUser = _admin.Email ?? mode;
        var code = await db.LicenseCodes.Include(c => c.Activations).FirstAsync(c => c.Id == preview.LicenseCodeId);

        var terms = BuildTerms(code, overrides?.Tier, overrides?.ExpiryDate);
        var termsProblem = terms.Validate();
        if (termsProblem is not null) return SerialKeyIssueResult.Fail(termsProblem);

        string serialKey;
        try
        {
            serialKey = SerialKey.Generate(terms, softCode!.ActivationCode, softCode.Fingerprint, _secrets.Current);
        }
        catch (InvalidOperationException ex)
        {
            Log.Error("Serial key generation failed (license secret missing or invalid?)", ex);
            return SerialKeyIssueResult.Fail(ex.Message);
        }

        RetireSameMachine(code, preview.FingerprintHex);
        db.ActivationRecords.Add(NewActivation(code, preview.FingerprintHex, mode, terms, appVersion,
            serialKey, CrockfordBase32.Normalize(softCodeText)));
        code.IsUsed = true;
        code.Status = LicenseCodeStatus.Active;
        await db.SaveChangesAsync();

        await _audit.LogAsync("SerialKey.Issued", new
        {
            preview.ActivationCode,
            Fingerprint = preview.FingerprintHex,
            Mode = mode,
            Tier = terms.Tier.ToString(),
            terms.ExpiryDate,
            Overridden = overrides is not null && (overrides.Tier is not null || overrides.ExpiryDate is not null),
        });

        return new SerialKeyIssueResult(true, serialKey, null, terms);
    }

    // ================================================================ online ====

    public async Task<OnlineLicenseResult> ActivateOnlineAsync(string activationCodeText, string fingerprintHex, string? appVersion)
    {
        if (_signingKey is null) return OnlineLicenseResult.Fail("The license server is not configured (no signing key).", "Error");
        if (!ActivationCode.TryParse(activationCodeText, out var code))
            return OnlineLicenseResult.Fail("That activation code isn't valid. Check it was typed exactly.");
        if (!TryParseFingerprint(fingerprintHex, out var fingerprint))
            return OnlineLicenseResult.Fail("The request from InfoID was not valid.");

        await using var db = await _dbFactory.CreateDbContextAsync();
        var preview = await BuildPreviewAsync(db, code!, fingerprint!, DateOnly.FromDateTime(DateTime.UtcNow));
        if (preview.Problems.Count > 0)
        {
            await _audit.LogAsync("Online.ActivationRefused", new { preview.ActivationCode, Fingerprint = fingerprint!.Hex, Problems = preview.Problems });
            return OnlineLicenseResult.Fail(string.Join(" ", preview.Problems));
        }

        db.CurrentUser = "LicenseApi";
        var entity = await db.LicenseCodes.Include(c => c.Activations).Include(c => c.CustomerRecord)
            .FirstAsync(c => c.Id == preview.LicenseCodeId);
        var terms = BuildTerms(entity, null, null);

        RetireSameMachine(entity, fingerprint!.Hex);
        var activation = NewActivation(entity, fingerprint.Hex, "Online", terms, appVersion, serialKey: null, softCode: null);
        activation.LastValidationDate = DateTime.UtcNow;
        db.ActivationRecords.Add(activation);
        entity.IsUsed = true;
        entity.Status = LicenseCodeStatus.Active;
        await db.SaveChangesAsync();

        await _audit.LogAsync("Online.Activated", new { preview.ActivationCode, Fingerprint = fingerprint.Hex, Tier = terms.Tier.ToString(), terms.ExpiryDate, appVersion });
        return new OnlineLicenseResult(true, Sign(activation.Id, entity, fingerprint.Hex, terms), null, "Active");
    }

    /// <summary>Periodic re-validation (FR-LIC-13). Returns a fresh license with the
    /// CURRENT terms (so an Extend in License Manager reaches the PC automatically), or
    /// a Revoked/Deactivated status so the PC stops printing.</summary>
    public async Task<OnlineLicenseResult> CheckInAsync(string activationCodeText, string fingerprintHex, int? cardsIssued, string? appVersion)
    {
        if (_signingKey is null) return OnlineLicenseResult.Fail("The license server is not configured (no signing key).", "Error");
        if (!ActivationCode.TryParse(activationCodeText, out var code) || !TryParseFingerprint(fingerprintHex, out var fingerprint))
            return OnlineLicenseResult.Fail("The request from InfoID was not valid.", "Unknown");

        await using var db = await _dbFactory.CreateDbContextAsync();
        db.CurrentUser = "LicenseApi";
        var entity = await db.LicenseCodes.Include(c => c.Activations).Include(c => c.CustomerRecord)
            .FirstOrDefaultAsync(c => c.ActivationCode == code!.Value && !c.Cancelled);
        if (entity is null)
            return OnlineLicenseResult.Fail("This license is not known to the license server.", "Unknown");

        if (entity.Status == LicenseCodeStatus.Revoked)
            return OnlineLicenseResult.Fail("This license has been revoked. Please contact your vendor.", "Revoked");

        var activation = entity.Activations
            .Where(a => a.MachineFingerprint == fingerprint!.Hex && a.Status == ActivationStatus.Active && !a.Cancelled)
            .OrderByDescending(a => a.Id)
            .FirstOrDefault();
        if (activation is null)
            return OnlineLicenseResult.Fail("This computer's license was deactivated. Activate again to continue.", "Deactivated");

        if (await IsBlacklistedAsync(db, fingerprint!.Hex, entity.ActivationCode))
            return OnlineLicenseResult.Fail("This license is blocked. Please contact your vendor.", "Revoked");

        // Keep any tier override given at issue time; take the latest expiry.
        var tier = Enum.Parse<LicenseTier>(activation.IssuedTier);
        var expiry = LaterOf(entity.ExpiryDate, activation.IssuedExpiryDate, entity.LicenseType);
        var terms = BuildTerms(entity, tier, expiry);

        activation.LastValidationDate = DateTime.UtcNow;
        activation.IssuedExpiryDate = terms.ExpiryDate;
        activation.CardsIssuedReported = cardsIssued;
        if (!string.IsNullOrWhiteSpace(appVersion)) activation.AppVersion = appVersion;
        await db.SaveChangesAsync();

        return new OnlineLicenseResult(true, Sign(activation.Id, entity, fingerprint.Hex, terms), null, "Active");
    }

    /// <summary>Deactivate / transfer from the customer's PC (FR-LIC-10): frees the slot.</summary>
    public async Task<(bool Succeeded, string? Error)> DeactivateOnlineAsync(string activationCodeText, string fingerprintHex)
    {
        if (!ActivationCode.TryParse(activationCodeText, out var code) || !TryParseFingerprint(fingerprintHex, out var fingerprint))
            return (false, "The request from InfoID was not valid.");

        await using var db = await _dbFactory.CreateDbContextAsync();
        db.CurrentUser = "LicenseApi";
        var entity = await db.LicenseCodes.Include(c => c.Activations)
            .FirstOrDefaultAsync(c => c.ActivationCode == code!.Value && !c.Cancelled);
        if (entity is null) return (false, "This license is not known to the license server.");

        var active = entity.Activations
            .Where(a => a.MachineFingerprint == fingerprint!.Hex && a.Status == ActivationStatus.Active && !a.Cancelled)
            .ToList();
        foreach (var activation in active)
        {
            activation.Status = ActivationStatus.Deactivated;
            activation.DeactivatedDate = DateTime.UtcNow;
            db.TransferHistories.Add(new TransferHistory
            {
                LicenseCodeId = entity.Id,
                OldActivationRecordId = activation.Id,
                TransferDate = DateTime.UtcNow,
                Reason = "Deactivated from InfoID",
            });
        }

        // Same rule as License Manager's "Free machine slots": a code with no machines
        // left in use goes back to Available (never touches Revoked codes).
        if (entity.Status == LicenseCodeStatus.Active &&
            !entity.Activations.Any(a => a.Status == ActivationStatus.Active && !a.Cancelled))
            entity.Status = LicenseCodeStatus.Available;

        await db.SaveChangesAsync();

        await _audit.LogAsync("Online.Deactivated", new { ActivationCode = entity.ActivationCode, Fingerprint = fingerprint!.Hex, Count = active.Count });
        return (true, null);
    }

    // ================================================================ shared rules ====

    private async Task<SoftCodePreview> BuildPreviewAsync(LicenseDbContext db, ActivationCode code, MachineFingerprint fingerprint, DateOnly requestDate)
    {
        var entity = await db.LicenseCodes.AsNoTracking()
            .Include(c => c.CustomerRecord)
            .Include(c => c.Activations)
            .FirstOrDefaultAsync(c => c.ActivationCode == code.Value && !c.Cancelled);

        var fingerprintHex = fingerprint.Hex;
        var problems = new List<string>();

        if (entity is null)
        {
            problems.Add("This activation code does not exist in the license database.");
            return new SoftCodePreview(code.ToString(), fingerprintHex, requestDate,
                null, null, null, null, null, null, null, null, 0, 0, false, false, problems);
        }

        var active = entity.Activations.Where(a => a.Status == ActivationStatus.Active && !a.Cancelled).ToList();
        var sameMachine = active.Any(a => a.MachineFingerprint == fingerprintHex);

        if (entity.Status == LicenseCodeStatus.Revoked)
            problems.Add("This activation code has been revoked.");
        if (!sameMachine && active.Count >= entity.MaxActivations)
            problems.Add($"All {entity.MaxActivations} activation slot(s) are in use. The customer must deactivate the old machine first, or use Transfer.");
        if (entity.ExpiryDate is { } expiry && expiry < DateOnly.FromDateTime(DateTime.UtcNow))
            problems.Add($"This license expired on {expiry:d}. Extend it before issuing a key.");
        if (await IsBlacklistedAsync(db, fingerprintHex, entity.ActivationCode))
            problems.Add("This machine or activation code is blacklisted.");
        if (requestDate < DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-30))
            problems.Add($"This request code is from {requestDate:d}, more than 30 days ago. Ask the customer for a new one if in doubt.");

        return new SoftCodePreview(
            code.ToString(), fingerprintHex, requestDate,
            entity.Id, entity.Status, entity.Tier, entity.LicenseType, entity.ExpiryDate, entity.MaxCardsPerPeriod, entity.CardLimitPeriod,
            entity.CustomerRecord?.OrganizationName, active.Count, entity.MaxActivations, sameMachine, entity.AllowOfflineActivation, problems);
    }

    private static SerialKeyTerms BuildTerms(LicenseCode code, LicenseTier? tierOverride, DateOnly? expiryOverride) => new(
        tierOverride ?? Enum.Parse<LicenseTier>(code.Tier),
        Enum.Parse<LicenseType>(code.LicenseType),
        expiryOverride ?? code.ExpiryDate,
        code.MaxCardsPerPeriod,
        Enum.Parse<CardLimitPeriod>(code.CardLimitPeriod));

    private static DateOnly? LaterOf(DateOnly? a, DateOnly? b, string licenseType)
    {
        if (licenseType == nameof(LicenseType.Perpetual)) return null;
        if (a is null) return b;
        if (b is null) return a;
        return a > b ? a : b;
    }

    /// <summary>Re-issuing for a machine that already holds this code (reinstall,
    /// renewal) replaces its record instead of using a new slot.</summary>
    private static void RetireSameMachine(LicenseCode code, string fingerprintHex)
    {
        foreach (var previous in code.Activations.Where(a => a.Status == ActivationStatus.Active && a.MachineFingerprint == fingerprintHex))
        {
            previous.Status = ActivationStatus.Deactivated;
            previous.DeactivatedDate = DateTime.UtcNow;
        }
    }

    private ActivationRecord NewActivation(LicenseCode code, string fingerprintHex, string mode, SerialKeyTerms terms,
        string? appVersion, string? serialKey, string? softCode) => new()
    {
        LicenseCodeId = code.Id,
        MachineFingerprint = fingerprintHex,
        SerialKeyIssued = serialKey,
        SoftCode = softCode,
        ActivationMode = mode,
        Status = ActivationStatus.Active,
        ActivationDate = DateTime.UtcNow,
        IssuedTier = terms.Tier.ToString(),
        IssuedExpiryDate = terms.ExpiryDate,
        AppVersion = appVersion,
        GeneratedByAdminUserId = mode == "Offline" ? _admin.CurrentAdmin?.Id : null,
    };

    private string Sign(long licenseId, LicenseCode code, string fingerprintHex, SerialKeyTerms terms)
    {
        var now = DateTime.UtcNow;
        return SignedLicense.Sign(new OnlineLicense
        {
            LicenseId = licenseId,
            ActivationCode = code.ActivationCode,
            Fingerprint = fingerprintHex,
            Tier = terms.Tier,
            Type = terms.Type,
            ExpiryDate = terms.ExpiryDate,
            MaxCardsPerPeriod = terms.MaxCardsPerPeriod,
            LimitPeriod = terms.LimitPeriod,
            OrganizationName = code.CustomerRecord?.OrganizationName,
            IssuedUtc = now,
            CheckInDeadlineUtc = now.Add(CheckInWindow),
        }, _signingKey!.PrivateKeyPem);
    }

    private static bool TryParseFingerprint(string? hex, out MachineFingerprint? fingerprint)
    {
        fingerprint = null;
        try
        {
            if (string.IsNullOrWhiteSpace(hex) || hex.Length != MachineFingerprint.HashLength * 2) return false;
            fingerprint = MachineFingerprint.FromHex(hex);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static Task<bool> IsBlacklistedAsync(LicenseDbContext db, string fingerprint, string activationCode) =>
        db.BlacklistEntries.AnyAsync(b => !b.Cancelled &&
            ((b.EntryType == "Fingerprint" && b.Value == fingerprint) ||
             (b.EntryType == "ActivationCode" && b.Value == activationCode)));
}
