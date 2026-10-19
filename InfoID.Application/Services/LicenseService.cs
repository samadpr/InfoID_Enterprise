using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using InfoID.Application.Dtos.Licensing;
using InfoID.Application.Interfaces;
using InfoID.Domain.Common.Enums;
using InfoID.Domain.Common.Interfaces;
using InfoID.Domain.Entities.LicensingModule;
using InfoID.Domain.Entities.OrganizationModule;
using InfoID.Domain.Entities.SystemModule;
using InfoID.Licensing.Core;
using InfoID.Shared.Common;
using InfoID.Shared.Contracts.Licensing;
using CoreTier = InfoID.Licensing.Core.LicenseTier;
using CoreType = InfoID.Licensing.Core.LicenseType;
using DomainTier = InfoID.Domain.Common.Enums.LicenseTier;
using DomainType = InfoID.Domain.Common.Enums.LicenseType;
using LicenseEntity = InfoID.Domain.Entities.LicensingModule.LicenseRecord;

namespace InfoID.Application.Services;

/// <summary>
/// Client-side licensing (SRS 4.7.1) on top of the local LicenseRecord table.
///
/// Tamper resistance (FR-SEC-3, FR-LIC-7): the database row is NOT trusted on its own.
/// Every check re-verifies the stored serial key against the stored activation code and
/// THIS machine's fingerprint (InfoID.Licensing.Core.SerialKey.Validate), and the tier,
/// type, expiry and card limit are taken from the verified key -- so editing the SQLite
/// file can't upgrade or extend a license, and copying it to another PC doesn't work.
/// The card counter is sealed with an HMAC so it can't be quietly reset either.
///
/// Licensing gates issuance/printing/encoding ONLY -- never opening the app or
/// navigation, and it never logs anyone out (FR-SEC-7).
/// </summary>
public class LicenseService : ILicenseService
{
    private const string PeriodSettingKey = "License.CardPeriod";
    private const string SealSettingKey = "License.CardCounterSeal";
    private const int DefaultGraceDays = 7;
    private const int DefaultRevalidationDays = 30;
    /// <summary>Online licenses check in at most this often in the background.</summary>
    private static readonly TimeSpan CheckInEvery = TimeSpan.FromDays(3);

    private readonly IUnitOfWork _uow;
    private readonly IMachineFingerprintProvider _fingerprint;
    private readonly ILicenseSecretProvider _secrets;
    private readonly ILicenseServerClient _server;
    private readonly Microsoft.Extensions.Logging.ILogger<LicenseService> _logger;
    private readonly IAuditTrail? _audit;

    public LicenseService(IUnitOfWork uow, IMachineFingerprintProvider fingerprint, ILicenseSecretProvider secrets, ILicenseServerClient server,
        Microsoft.Extensions.Logging.ILogger<LicenseService>? logger = null, IAuditTrail? audit = null)
    {
        _audit = audit;
        // Optional so existing callers/tests that construct the service directly still work.
        // Never log activation codes, serial keys or the license secret -- only what failed.
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<LicenseService>.Instance;
        _uow = uow;
        _fingerprint = fingerprint;
        _secrets = secrets;
        _server = server;
    }

    // ------------------------------------------------------------------ status ----

    public async Task<LicenseStatusDto> GetCurrentStatusAsync(CancellationToken ct = default)
    {
        var license = await LoadVerifiedAsync(ct);
        if (license is null)
            return new LicenseStatusDto { HasLicense = false, Problem = "InfoID is not activated on this computer." };

        var record = license.Record;
        var dto = new LicenseStatusDto
        {
            HasLicense = true,
            IsValid = license.Terms is not null,
            Problem = license.Problem,
            ActivationCode = FormatActivationCode(record.ActivationCode),
            MachineIdMasked = SafeMachineIdMasked(),
            OrganizationName = record.Organization?.Name,
            ActivationDate = record.ActivationDate,
            ActivationMode = record.ActivationMode,
            LastConfirmedOnline = record.ActivationMode == ActivationMode.Online ? record.LastValidationDate : null,
            CheckInDeadline = license.Online?.CheckInDeadlineUtc,
        };

        if (license.Terms is not { } terms)
        {
            // Deactivated by the user/server is a normal state, not a broken license.
            if (record.Status == LicenseStatus.Deactivated) dto.Status = LicenseStatus.Deactivated;
            return dto;
        }

        await RollPeriodIfNeededAsync(record, terms, ct);
        var (inGrace, graceLeft, expired) = ComputeExpiry(terms.ExpiryDate, record.GracePeriodDays);

        dto.Tier = ToDomain(terms.Tier);
        dto.Type = ToDomain(terms.Type);
        dto.ExpiryDate = ExpiryUtc(terms.ExpiryDate);
        dto.DaysUntilExpiry = terms.ExpiryDate is { } e ? e.DayNumber - DateOnly.FromDateTime(DateTime.UtcNow).DayNumber : null;
        dto.MaxCardsPerPeriod = terms.MaxCardsPerPeriod;
        dto.CardLimitPeriod = terms.MaxCardsPerPeriod is null ? null : terms.LimitPeriod.ToString();
        dto.CardsIssuedThisPeriod = record.CardsIssuedThisPeriod;
        dto.IsInGracePeriod = inGrace;
        dto.GracePeriodDaysRemaining = graceLeft;
        dto.Status = record.Status == LicenseStatus.Deactivated ? LicenseStatus.Deactivated
            : inGrace ? LicenseStatus.GracePeriod
            : expired ? LicenseStatus.Expired
            : LicenseStatus.Active;
        return dto;
    }

    public async Task<FeatureCheckResult> CheckFeatureAllowedAsync(string featureName, int cardCount = 0, CancellationToken ct = default)
    {
        var license = await LoadVerifiedAsync(ct);
        if (license is null)
            return FeatureCheckResult.Denied("InfoID is not activated on this computer. Open Help > Account and license to activate.");
        if (license.Terms is not { } terms)
            return FeatureCheckResult.Denied(license.Problem ?? "The license on this computer is not valid.");

        var record = license.Record;
        if (record.Status == LicenseStatus.Deactivated)
            return FeatureCheckResult.Denied("This license has been deactivated on this computer.");

        var (_, _, expired) = ComputeExpiry(terms.ExpiryDate, record.GracePeriodDays);
        if (expired)
            return FeatureCheckResult.Denied($"Your license expired on {terms.ExpiryDate:d} and the {record.GracePeriodDays}-day grace period is over. " +
                                             "Renew to continue printing and issuing cards. Your designs and data remain available.");

        var tier = ToDomain(terms.Tier);
        var needed = LicensedFeatures.MinimumTier(featureName);
        if (tier < needed)
            return FeatureCheckResult.Denied($"This feature needs an InfoID {needed} license. Your license is {tier}.");

        if (!LicensedFeatures.CountsCards(featureName) || terms.MaxCardsPerPeriod is not { } max)
            return FeatureCheckResult.Allowed();

        await RollPeriodIfNeededAsync(record, terms, ct);
        if (!await IsCounterSealValidAsync(record, ct))
            return FeatureCheckResult.Denied("The card usage record on this computer was modified outside InfoID. Please contact support.");

        var remaining = Math.Max(0, max - record.CardsIssuedThisPeriod);
        if (remaining == 0)
            return FeatureCheckResult.Denied($"The card limit of {max:N0} per {PeriodWord(terms.LimitPeriod)} has been reached.");
        if (cardCount > remaining)
            return FeatureCheckResult.Denied($"This job needs {cardCount:N0} cards but only {remaining:N0} of your {max:N0} per {PeriodWord(terms.LimitPeriod)} are left.");

        return FeatureCheckResult.Allowed(remaining);
    }

    public async Task IncrementCardsIssuedAsync(int count = 1, CancellationToken ct = default)
    {
        if (count <= 0) return;
        var license = await LoadVerifiedAsync(ct);
        if (license?.Terms is not { } terms) return;

        var record = license.Record;
        await RollPeriodIfNeededAsync(record, terms, ct);
        record.CardsIssuedThisPeriod += count;
        record.ModifiedDate = DateTime.UtcNow;
        _uow.Repository<LicenseEntity>().Update(record);
        await WriteSealAsync(record, await GetSettingAsync(PeriodSettingKey, ct) ?? "", ct);
        await _uow.SaveChangesAsync(ct);
    }

    // ------------------------------------------------------------------ activation ----

    public Task<Result<RequestCodeDto>> CreateRequestCodeAsync(string activationCode, CancellationToken ct = default)
    {
        if (!Licensing.Core.ActivationCode.TryParse(activationCode, out var code))
            return Task.FromResult(Result<RequestCodeDto>.Failure(
                "That activation code isn't valid. Check it was typed exactly (16 letters and numbers, e.g. 7K3M-QX9D-2HFA-P8ZR)."));

        MachineFingerprint fingerprint;
        try
        {
            fingerprint = _fingerprint.GetFingerprint();
        }
        catch (Exception ex)
        {
            Microsoft.Extensions.Logging.LoggerExtensions.LogError(_logger, ex, "Machine fingerprint failed while creating a request code");
            return Task.FromResult(Result<RequestCodeDto>.Failure("InfoID couldn't read this computer's hardware identity: " + ex.Message));
        }

        var requestCode = SoftCode.Create(code!, fingerprint).Encode();
        return Task.FromResult(Result<RequestCodeDto>.Success(new RequestCodeDto
        {
            ActivationCode = code!.ToString(),
            RequestCode = requestCode,
            MachineIdMasked = fingerprint.Masked,
            PhoneActivationUrl = _server.IsConfigured ? _server.GetPhoneActivationUrl(requestCode) : null,
        }));
    }

    public async Task<Result<LicenseStatusDto>> ActivateOfflineAsync(string activationCode, string serialKey, ActivationDetailsDto? details = null, CancellationToken ct = default)
    {
        if (!Licensing.Core.ActivationCode.TryParse(activationCode, out var code))
            return Result<LicenseStatusDto>.Failure("That activation code isn't valid. Check it was typed exactly.");

        var secrets = _secrets.GetSecrets();
        if (secrets.Count == 0)
            return Result<LicenseStatusDto>.Failure("This build of InfoID has no license verification key configured. Please contact support.");

        var validation = SerialKey.Validate(serialKey, code!, _fingerprint.GetFingerprint(), secrets);
        if (!validation.IsValid)
            return Result<LicenseStatusDto>.Failure(validation.Error ?? "The serial key is not valid.");

        var terms = validation.Terms!;
        var (_, _, expired) = ComputeExpiry(terms.ExpiryDate, DefaultGraceDays);
        if (expired)
            return Result<LicenseStatusDto>.Failure($"This serial key's license expired on {terms.ExpiryDate:d}. Please contact your vendor for a renewed key.");

        var normalizedKey = Licensing.Core.Codecs.CrockfordBase32.Group(Licensing.Core.Codecs.CrockfordBase32.Normalize(serialKey), 5);
        await SaveLicenseAsync(code!, normalizedKey, terms, ActivationMode.Offline, details, organizationFromServer: null, ct);
        return Result<LicenseStatusDto>.Success(await GetCurrentStatusAsync(ct));
    }

    public async Task<OnlineActivationResult> ActivateOnlineAsync(string activationCode, CancellationToken ct = default)
    {
        if (!Licensing.Core.ActivationCode.TryParse(activationCode, out var code))
            return new OnlineActivationResult { Error = "That activation code isn't valid. Check it was typed exactly (16 letters and numbers, e.g. 7K3M-QX9D-2HFA-P8ZR)." };
        if (!_server.IsConfigured)
            return new OnlineActivationResult { Error = "Online activation isn't available in this version. Use offline activation.", IsServerUnreachable = true };
        if (_secrets.GetSigningPublicKeyPem() is not { } publicKey)
            return new OnlineActivationResult { Error = "This build of InfoID has no license verification key configured. Please contact support." };

        MachineFingerprint fingerprint;
        try
        {
            fingerprint = _fingerprint.GetFingerprint();
        }
        catch (Exception ex)
        {
            Microsoft.Extensions.Logging.LoggerExtensions.LogError(_logger, ex, "Machine fingerprint failed during online activation");
            return new OnlineActivationResult { Error = "InfoID couldn't read this computer's hardware identity: " + ex.Message };
        }

        OnlineActivationResponse response;
        try
        {
            response = await _server.ActivateAsync(new OnlineActivationRequest
            {
                ActivationCode = code!.Value,
                MachineFingerprint = fingerprint.Hex,
                AppVersion = AppVersion(),
            }, ct);
        }
        catch (LicenseServerUnavailableException ex)
        {
            Microsoft.Extensions.Logging.LoggerExtensions.LogWarning(_logger, ex, "License server unreachable during online activation");
            return new OnlineActivationResult { Error = ex.Message, IsServerUnreachable = true };
        }

        if (!response.Success)
            return new OnlineActivationResult { Error = response.ErrorMessage ?? "The license server refused the activation." };

        var online = SignedLicense.Verify(response.License, publicKey, out var error);
        if (online is null || !string.Equals(online.Fingerprint, fingerprint.Hex, StringComparison.OrdinalIgnoreCase)
                           || online.ActivationCode != code!.Value)
            return new OnlineActivationResult { Error = "The license received from the server could not be verified" + (error is null ? "." : ": " + error) };

        await SaveLicenseAsync(code!, response.License!, online.ToTerms(), ActivationMode.Online, null, online.OrganizationName, ct);
        return new OnlineActivationResult { Succeeded = true, Status = await GetCurrentStatusAsync(ct) };
    }

    public async Task<Result<string>> CheckInAsync(bool force = false, CancellationToken ct = default)
    {
        var record = await LatestRecordAsync(ct);
        if (record is null || record.Status == LicenseStatus.Deactivated || !SignedLicense.IsSignedLicense(record.SerialKey))
            return Result<string>.Success("Nothing to check.");
        if (!force && record.LastValidationDate is { } last && DateTime.UtcNow - last < CheckInEvery)
            return Result<string>.Success("Checked recently.");
        if (!_server.IsConfigured || _secrets.GetSigningPublicKeyPem() is not { } publicKey)
            return Result<string>.Failure("Online license checks aren't available in this version.");

        LicenseCheckInResponse response;
        try
        {
            response = await _server.CheckInAsync(new LicenseCheckInRequest
            {
                ActivationCode = record.ActivationCode,
                MachineFingerprint = _fingerprint.GetFingerprint().Hex,
                CardsIssuedThisPeriod = record.CardsIssuedThisPeriod,
                AppVersion = AppVersion(),
            }, ct);
        }
        catch (LicenseServerUnavailableException ex)
        {
            Microsoft.Extensions.Logging.LoggerExtensions.LogWarning(_logger, ex, "License check-in: server unreachable, using the cached license");
            await LogValidationAsync(record, ValidationResult.OfflineCached, "Server unreachable: " + ex.Message, ct);
            return Result<string>.Failure(ex.Message);
        }

        switch (response.Status)
        {
            case "Active":
            {
                var online = SignedLicense.Verify(response.License, publicKey, out var error);
                if (online is null || online.ActivationCode != record.ActivationCode)
                {
                    await LogValidationAsync(record, ValidationResult.Failed, "Invalid license from server: " + error, ct);
                    return Result<string>.Failure("The license server's reply could not be verified.");
                }

                var terms = online.ToTerms();
                var beforeCheckIn = AuditSnapshot(record);
                var oldTier = record.LicenseTier; var oldExpiry = record.ExpiryDate; var oldMax = record.MaxCardsPerPeriod; var oldType = record.LicenseType;
                record.SerialKey = response.License;
                record.LicenseTier = ToDomain(terms.Tier);
                record.LicenseType = ToDomain(terms.Type);
                record.ExpiryDate = ExpiryUtc(terms.ExpiryDate);
                record.MaxCardsPerPeriod = terms.MaxCardsPerPeriod;
                record.LastValidationDate = DateTime.UtcNow;
                record.ModifiedDate = DateTime.UtcNow;
                _uow.Repository<LicenseEntity>().Update(record);
                await LogValidationAsync(record, ValidationResult.Success, "Online check-in", ct);
                if (oldTier != record.LicenseTier || oldExpiry != record.ExpiryDate || oldMax != record.MaxCardsPerPeriod || oldType != record.LicenseType)
                    await AuditAsync("UpdatedByServer",
                        $"License updated by the license server: {record.LicenseTier} {record.LicenseType}" +
                        (record.ExpiryDate is { } newExpiry ? $", valid until {newExpiry:d}" : ""),
                        record.Id, beforeCheckIn, AuditSnapshot(record), ct);
                return Result<string>.Success("License confirmed" + (terms.ExpiryDate is { } e ? $", valid until {e:d}." : "."));
            }

            case "Revoked":
            case "Deactivated":
                var beforeRevoke = AuditSnapshot(record);
                record.Status = LicenseStatus.Deactivated;
                record.ModifiedDate = DateTime.UtcNow;
                _uow.Repository<LicenseEntity>().Update(record);
                await LogValidationAsync(record, ValidationResult.Failed, $"{response.Status}: {response.Message}", ct);
                await AuditAsync("RevokedByServer", $"License {response.Status.ToLowerInvariant()} by the license server" +
                    (string.IsNullOrWhiteSpace(response.Message) ? "" : ": " + response.Message), record.Id, beforeRevoke, AuditSnapshot(record), ct);
                return Result<string>.Failure(response.Message ?? "This license is no longer active.");

            default:
                await LogValidationAsync(record, ValidationResult.Failed, $"{response.Status}: {response.Message}", ct);
                return Result<string>.Failure(response.Message ?? "The license server could not confirm this license.");
        }
    }

    /// <summary>Stores a verified license (online or offline) as the one active license.</summary>
    private async Task SaveLicenseAsync(Licensing.Core.ActivationCode code, string keyOrToken, SerialKeyTerms terms,
        ActivationMode mode, ActivationDetailsDto? details, string? organizationFromServer, CancellationToken ct)
    {
        var organization = await UpsertOrganizationAsync(details, organizationFromServer, ct);
        if (details is not null && !string.IsNullOrWhiteSpace(details.UserFullName))
            await EnsureUserAsync(organization, details, ct);

        // One active license per machine: retire earlier records (history kept).
        var licenses = _uow.Repository<LicenseEntity>();
        var previous = (await licenses.GetAllAsync(ct)).OrderByDescending(l => l.CreatedDate).FirstOrDefault();
        var before = previous is null ? null : AuditSnapshot(previous);
        foreach (var old in await licenses.GetAllAsync(ct))
            licenses.SoftDelete(old);

        var record = new LicenseEntity
        {
            Organization = organization,
            ActivationCode = code.Value,
            SerialKey = keyOrToken,
            MachineFingerprint = _fingerprint.GetFingerprint().Hex,
            LicenseTier = ToDomain(terms.Tier),
            LicenseType = ToDomain(terms.Type),
            ActivationMode = mode,
            ActivationDate = DateTime.UtcNow,
            ExpiryDate = ExpiryUtc(terms.ExpiryDate),
            MaxCardsPerPeriod = terms.MaxCardsPerPeriod,
            CardsIssuedThisPeriod = 0,
            Status = LicenseStatus.Active,
            GracePeriodDays = DefaultGraceDays,
            LastValidationDate = DateTime.UtcNow,
            RevalidationIntervalDays = DefaultRevalidationDays,
        };
        await licenses.AddAsync(record, ct);

        await _uow.Repository<LicenseValidationLog>().AddAsync(new LicenseValidationLog
        {
            LicenseRecord = record,
            ValidationDate = DateTime.UtcNow,
            ValidationResult = ValidationResult.Success,
            Notes = mode == ActivationMode.Online ? "Online activation" : "Offline activation",
        }, ct);

        var period = PeriodKey(terms.LimitPeriod);
        await SetSettingAsync(PeriodSettingKey, period, ct);
        await WriteSealAsync(record, period, ct);
        await _uow.SaveChangesAsync(ct);

        await AuditAsync("Activated",
            $"License activated ({(mode == ActivationMode.Online ? "online" : "offline")}): {record.LicenseTier} {record.LicenseType}" +
            (record.ExpiryDate is { } expiry ? $", valid until {expiry:d}" : ""),
            record.Id, before, AuditSnapshot(record), ct);
    }

    /// <summary>What the audit log keeps about a license. Never the activation code, the
    /// serial key or the machine fingerprint.</summary>
    private static object AuditSnapshot(LicenseEntity l) => new
    {
        Tier = l.LicenseTier.ToString(),
        Type = l.LicenseType.ToString(),
        Mode = l.ActivationMode?.ToString(),
        Status = l.Status.ToString(),
        ExpiryDate = l.ExpiryDate?.ToString("yyyy-MM-dd"),
        l.MaxCardsPerPeriod,
    };

    private Task AuditAsync(string action, string description, long? licenseId, object? before, object? after, CancellationToken ct) =>
        _audit is null
            ? Task.CompletedTask
            : _audit.RecordAsync(new AuditEntry(AuditModules.License, action, description)
            {
                EntityName = "LicenseRecord", EntityId = licenseId, Before = before, After = after,
            }, ct);

    private async Task LogValidationAsync(LicenseEntity record, ValidationResult result, string notes, CancellationToken ct)
    {
        await _uow.Repository<LicenseValidationLog>().AddAsync(new LicenseValidationLog
        {
            LicenseRecordId = record.Id,
            ValidationDate = DateTime.UtcNow,
            ValidationResult = result,
            Notes = notes.Length > 500 ? notes[..500] : notes,
        }, ct);
        await _uow.SaveChangesAsync(ct);
    }

    private static string AppVersion() =>
        typeof(LicenseService).Assembly.GetName().Version?.ToString() ?? "unknown";

    public async Task<Result<DeactivationInfoDto>> DeactivateAsync(CancellationToken ct = default)
    {
        var record = await LatestRecordAsync(ct);
        if (record is null)
            return Result<DeactivationInfoDto>.Failure("There is no license on this computer to deactivate.");

        // Online licenses free their machine slot on the server, so the customer can
        // activate on the new computer straight away. That needs the server: without
        // it the slot would stay taken, so don't deactivate locally either.
        if (SignedLicense.IsSignedLicense(record.SerialKey) && record.Status != LicenseStatus.Deactivated)
        {
            try
            {
                var response = await _server.DeactivateAsync(new LicenseDeactivationRequest
                {
                    ActivationCode = record.ActivationCode,
                    MachineFingerprint = _fingerprint.GetFingerprint().Hex,
                }, ct);
                if (!response.Success)
                    return Result<DeactivationInfoDto>.Failure(response.ErrorMessage ?? "The license server refused the deactivation.");
            }
            catch (LicenseServerUnavailableException ex)
            {
                Microsoft.Extensions.Logging.LoggerExtensions.LogWarning(_logger, ex, "License deactivation: server unreachable");
                return Result<DeactivationInfoDto>.Failure(
                    "InfoID needs an internet connection to deactivate this license, so it can be moved to another computer. " + ex.Message);
            }
        }

        var beforeDeactivate = AuditSnapshot(record);
        record.Status = LicenseStatus.Deactivated;
        record.ModifiedDate = DateTime.UtcNow;
        _uow.Repository<LicenseEntity>().Update(record);
        await _uow.Repository<LicenseValidationLog>().AddAsync(new LicenseValidationLog
        {
            LicenseRecordId = record.Id,
            ValidationDate = DateTime.UtcNow,
            ValidationResult = ValidationResult.Success,
            Notes = "Deactivated by user",
        }, ct);
        await _uow.SaveChangesAsync(ct);
        await AuditAsync("Deactivated", "License deactivated on this computer (ready to move to another PC)",
            record.Id, beforeDeactivate, AuditSnapshot(record), ct);

        return Result<DeactivationInfoDto>.Success(new DeactivationInfoDto
        {
            ActivationCode = FormatActivationCode(record.ActivationCode),
            MachineId = record.MachineFingerprint,
        });
    }

    // ------------------------------------------------------------------ verification ----

    private sealed record VerifiedLicense(LicenseEntity Record, SerialKeyTerms? Terms, string? Problem, OnlineLicense? Online = null);

    private async Task<LicenseEntity?> LatestRecordAsync(CancellationToken ct)
    {
        var all = await _uow.Repository<LicenseEntity>().GetAllAsync(ct);
        return all.OrderByDescending(l => l.CreatedDate).FirstOrDefault();
    }

    /// <summary>Loads the newest license and re-verifies its serial key for this
    /// machine. Terms are null (with a reason) when the record doesn't verify.</summary>
    private async Task<VerifiedLicense?> LoadVerifiedAsync(CancellationToken ct)
    {
        var record = await LatestRecordAsync(ct);
        if (record is null) return null;

        if (record.Organization is null)
            record.Organization = await _uow.Repository<Organization>().GetByIdAsync(record.OrganizationId, ct);

        if (record.Status == LicenseStatus.Deactivated)
            return new VerifiedLicense(record, null, "The license was deactivated on this computer. Activate again to continue printing.");

        if (!Licensing.Core.ActivationCode.TryParse(record.ActivationCode, out var code))
            return new VerifiedLicense(record, null, "The stored license is damaged. Please activate InfoID again.");

        MachineFingerprint fingerprint;
        try
        {
            fingerprint = _fingerprint.GetFingerprint();
        }
        catch (Exception ex)
        {
            Microsoft.Extensions.Logging.LoggerExtensions.LogError(_logger, ex, "Machine fingerprint failed while verifying the license");
            return new VerifiedLicense(record, null, "InfoID couldn't read this computer's hardware identity: " + ex.Message);
        }

        if (SignedLicense.IsSignedLicense(record.SerialKey))
            return VerifyOnline(record, code!, fingerprint);

        var result = SerialKey.Validate(record.SerialKey, code!, fingerprint, _secrets.GetSecrets());
        if (!result.IsValid)
        {
            var problem = !string.Equals(record.MachineFingerprint, fingerprint.Hex, StringComparison.OrdinalIgnoreCase)
                ? "This license belongs to a different computer. Activate InfoID on this computer with your activation code."
                : "The stored license could not be verified. Please activate InfoID again.";
            return new VerifiedLicense(record, null, problem);
        }

        return new VerifiedLicense(record, result.Terms, null);
    }

    /// <summary>Online license: ECDSA signature (public key only in InfoID), this
    /// machine, this code, and the check-in deadline (FR-LIC-13).</summary>
    private VerifiedLicense VerifyOnline(LicenseEntity record, Licensing.Core.ActivationCode code, MachineFingerprint fingerprint)
    {
        if (_secrets.GetSigningPublicKeyPem() is not { } publicKey)
            return new VerifiedLicense(record, null, "This build of InfoID has no license verification key configured. Please contact support.");

        var online = SignedLicense.Verify(record.SerialKey, publicKey, out _);
        if (online is null || online.ActivationCode != code.Value)
            return new VerifiedLicense(record, null, "The stored license could not be verified. Please activate InfoID again.");
        if (!string.Equals(online.Fingerprint, fingerprint.Hex, StringComparison.OrdinalIgnoreCase))
            return new VerifiedLicense(record, null, "This license belongs to a different computer. Activate InfoID on this computer with your activation code.");
        if (DateTime.UtcNow > online.CheckInDeadlineUtc)
            return new VerifiedLicense(record, null,
                $"InfoID hasn't been able to confirm your license online since {record.LastValidationDate?.ToLocalTime():d}. " +
                "Connect to the internet and click Check now on the License page. Your data is safe; only printing is paused.", online);

        return new VerifiedLicense(record, online.ToTerms(), null, online);
    }

    // ------------------------------------------------------------------ card period & seal ----

    private static string PeriodKey(CardLimitPeriod period) =>
        period == CardLimitPeriod.Yearly
            ? DateTime.UtcNow.Year.ToString(CultureInfo.InvariantCulture)
            : DateTime.UtcNow.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    private static string PeriodWord(CardLimitPeriod period) => period == CardLimitPeriod.Yearly ? "year" : "month";

    /// <summary>Resets the counter when a new month/year starts (FR-LIC-12).</summary>
    private async Task RollPeriodIfNeededAsync(LicenseEntity record, SerialKeyTerms terms, CancellationToken ct)
    {
        if (terms.MaxCardsPerPeriod is null) return;

        var current = PeriodKey(terms.LimitPeriod);
        var stored = await GetSettingAsync(PeriodSettingKey, ct);
        if (stored == current) return;

        record.CardsIssuedThisPeriod = 0;
        record.ModifiedDate = DateTime.UtcNow;
        _uow.Repository<LicenseEntity>().Update(record);
        await SetSettingAsync(PeriodSettingKey, current, ct);
        await WriteSealAsync(record, current, ct);
        await _uow.SaveChangesAsync(ct);
    }

    private async Task<bool> IsCounterSealValidAsync(LicenseEntity record, CancellationToken ct)
    {
        var stored = await GetSettingAsync(SealSettingKey, ct);
        var period = await GetSettingAsync(PeriodSettingKey, ct) ?? "";
        return stored is not null && CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(stored), Encoding.ASCII.GetBytes(ComputeSeal(record, period)));
    }

    /// <summary>The period is passed in (not re-read) because it may have been set in
    /// this same unsaved unit of work.</summary>
    private async Task WriteSealAsync(LicenseEntity record, string period, CancellationToken ct) =>
        await SetSettingAsync(SealSettingKey, ComputeSeal(record, period), ct);

    /// <summary>HMAC over activation code + period + count with the license secret, so
    /// lowering CardsIssuedThisPeriod in the database is detected.</summary>
    private string ComputeSeal(LicenseEntity record, string period)
    {
        var secret = _secrets.GetSecrets().OrderByDescending(s => s.Version).FirstOrDefault();
        if (secret is null) return string.Empty;
        var data = Encoding.UTF8.GetBytes($"InfoIDCardSeal|{record.ActivationCode}|{period}|{record.CardsIssuedThisPeriod}");
        return Convert.ToHexString(HMACSHA256.HashData(secret.Key, data));
    }

    private async Task<string?> GetSettingAsync(string key, CancellationToken ct)
    {
        var setting = await _uow.Repository<SystemSetting>().SingleOrDefaultAsync(s => s.SettingKey == key && s.OrganizationId == null, ct);
        return setting?.SettingValue;
    }

    private async Task SetSettingAsync(string key, string value, CancellationToken ct)
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

    // ------------------------------------------------------------------ organization / user ----

    private async Task<Organization> UpsertOrganizationAsync(ActivationDetailsDto? details, string? organizationFromServer, CancellationToken ct)
    {
        var repo = _uow.Repository<Organization>();
        var existing = (await repo.GetAllAsync(ct)).OrderBy(o => o.CreatedDate).FirstOrDefault();
        if (existing is null)
        {
            existing = new Organization();
            await repo.AddAsync(existing, ct);
        }
        else
        {
            existing.ModifiedDate = DateTime.UtcNow;
            repo.Update(existing);
        }

        // Typed details win; otherwise the organization name registered with the vendor
        // (online activation); otherwise keep what's there.
        existing.Name = NullIfEmpty(details?.OrganizationName) ?? NullIfEmpty(organizationFromServer)
                        ?? NullIfEmpty(existing.Name) ?? "My Organization";
        existing.Email = NullIfEmpty(details?.OrganizationEmail) ?? existing.Email;
        existing.Country = NullIfEmpty(details?.Country) ?? existing.Country;
        existing.OrgType = details?.InstallationPurpose ?? existing.OrgType;
        return existing;
    }

    private async Task EnsureUserAsync(Organization organization, ActivationDetailsDto details, CancellationToken ct)
    {
        var repo = _uow.Repository<AppUser>();
        var user = (await repo.GetAllAsync(ct)).OrderBy(u => u.CreatedDate).FirstOrDefault();
        if (user is null)
        {
            await repo.AddAsync(new AppUser
            {
                Organization = organization,
                FullName = details.UserFullName.Trim(),
                Email = NullIfEmpty(details.OrganizationEmail),
                Phone = NullIfEmpty(details.UserPhone),
            }, ct);
            return;
        }

        user.FullName = details.UserFullName.Trim();
        user.Phone = NullIfEmpty(details.UserPhone) ?? user.Phone;
        user.ModifiedDate = DateTime.UtcNow;
        repo.Update(user);
    }

    // ------------------------------------------------------------------ helpers ----

    /// <summary>Expiry dates are whole days: the license works through the end of the
    /// expiry day (UTC), then the grace period starts (FR-LIC-12).</summary>
    private static DateTime? ExpiryUtc(DateOnly? date) =>
        date is { } d ? d.ToDateTime(new TimeOnly(23, 59, 59), DateTimeKind.Utc) : null;

    private static (bool inGrace, int? graceDaysLeft, bool expired) ComputeExpiry(DateOnly? expiry, int graceDays)
    {
        if (ExpiryUtc(expiry) is not { } end) return (false, null, false);
        var now = DateTime.UtcNow;
        if (now <= end) return (false, null, false);

        var graceEnd = end.AddDays(graceDays);
        if (now > graceEnd) return (false, 0, true);
        return (true, (int)Math.Ceiling((graceEnd - now).TotalDays), false);
    }

    private static DomainTier ToDomain(CoreTier tier) => Enum.Parse<DomainTier>(tier.ToString());
    private static DomainType ToDomain(CoreType type) => Enum.Parse<DomainType>(type.ToString());

    private static string FormatActivationCode(string stored) =>
        Licensing.Core.ActivationCode.TryParse(stored, out var code) ? code!.ToString() : stored;

    private string? SafeMachineIdMasked()
    {
        try { return _fingerprint.GetFingerprint().Masked; }
        catch { return null; }
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
