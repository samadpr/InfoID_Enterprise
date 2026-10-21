using InfoID.Licensing.Core;
using InfoID.Licensing.Core.Codecs;
using InfoID.LicenseManager.Application.Common;
using InfoID.LicenseManager.Application.DTOs.Activations;
using InfoID.LicenseManager.Application.Interfaces.Repositories;
using InfoID.LicenseManager.Application.Interfaces.Security;
using InfoID.LicenseManager.Application.Interfaces.Services;
using InfoID.LicenseManager.Domain.Entities;
using InfoID.LicenseManager.Domain.Rules;
using InfoID.Shared.Common;
using Microsoft.Extensions.Logging;

namespace InfoID.LicenseManager.Application.Services;

public sealed class ActivationService : IActivationService
{
    public const string OfflineMode = "Offline";
    public const string PhoneMode = "Phone";

    private const string UnknownCodeMessage = "This activation code does not exist in the license database.";

    private readonly ILicenseRepository _licenses;
    private readonly IBlacklistRepository _blacklist;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILicenseSecretProvider _secrets;
    private readonly IAuditService _audit;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _time;
    private readonly ILogger<ActivationService> _logger;

    public ActivationService(ILicenseRepository licenses, IBlacklistRepository blacklist, IUnitOfWork unitOfWork,
        ILicenseSecretProvider secrets, IAuditService audit, ICurrentUser currentUser, TimeProvider time, ILogger<ActivationService> logger)
    {
        _licenses = licenses;
        _blacklist = blacklist;
        _unitOfWork = unitOfWork;
        _secrets = secrets;
        _audit = audit;
        _currentUser = currentUser;
        _time = time;
        _logger = logger;
    }

    private DateTime UtcNow => _time.GetUtcNow().UtcDateTime;

    private DateOnly Today => DateOnly.FromDateTime(UtcNow);

    public async Task<Result<SoftCodePreviewDto>> PreviewAsync(string softCodeText, CancellationToken cancellationToken = default)
    {
        _currentUser.RequireSerialKeyGeneration();

        if (!SoftCode.TryParse(softCodeText, out var softCode, out var error))
            return Result<SoftCodePreviewDto>.Failure(error ?? "Invalid request code.");

        var code = await _licenses.FindByActivationCodeAsync(softCode!.ActivationCode.Value, cancellationToken);
        return Result<SoftCodePreviewDto>.Success(await BuildPreviewAsync(code, softCode, cancellationToken));
    }

    public Task<Result<SerialKeyIssuedDto>> IssueSerialKeyAsync(IssueSerialKeyRequestDto request, CancellationToken cancellationToken = default)
    {
        _currentUser.RequireSerialKeyGeneration();
        if (request.TierOverride is not null || request.ExpiryOverride is not null)
            _currentUser.RequireManageLicenses("override tier or expiry");

        return IssueCoreAsync(request, OfflineMode, requireOfflineAllowed: false, cancellationToken);
    }

    /// <summary>Self-service phone/QR page: same checks, no staff, no overrides, and only for codes where staff
    /// enabled offline activation.</summary>
    public Task<Result<SerialKeyIssuedDto>> IssueForPhoneAsync(string softCodeText, CancellationToken cancellationToken = default)
    {
        _unitOfWork.UseActor(PhoneMode);
        return IssueCoreAsync(new IssueSerialKeyRequestDto(softCodeText, null, null), PhoneMode, requireOfflineAllowed: true, cancellationToken);
    }

    private async Task<Result<SerialKeyIssuedDto>> IssueCoreAsync(IssueSerialKeyRequestDto request, string mode, bool requireOfflineAllowed, CancellationToken cancellationToken)
    {
        if (!SoftCode.TryParse(request.SoftCode, out var softCode, out var parseError))
            return Result<SerialKeyIssuedDto>.Failure(parseError ?? "Invalid request code.");
        if (request.TierOverride is { } tier && !Enum.IsDefined(tier))
            return Result<SerialKeyIssuedDto>.Failure("Choose a valid edition.");
        if (!_secrets.IsConfigured)
        {
            _logger.LogError("Serial key requested but no license secret is configured");
            return Result<SerialKeyIssuedDto>.Failure(
                "No license secret is configured on the server, so serial keys cannot be generated. Set INFOID_LICENSE_SECRET_V1 and restart.");
        }

        var activationCode = softCode!.ActivationCode.Value;
        var fingerprint = softCode.Fingerprint;
        var normalizedRequest = CrockfordBase32.Normalize(request.SoftCode);

        // Slot check and the new activation under one row lock: two simultaneous requests cannot both take the last slot.
        var outcome = await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var license = await _licenses.FindForUpdateByActivationCodeAsync(activationCode, ct);
            if (license is null) return IssueOutcome.Fail(UnknownCodeMessage);

            var blacklisted = await _blacklist.IsBlacklistedAsync(fingerprint.Hex, license.ActivationCode, ct);
            var blocking = ActivationRules.Evaluate(license, fingerprint.Hex, blacklisted, softCode.RequestDate, Today)
                .Where(p => p.IsBlocking).Select(p => p.Message).ToList();
            if (blocking.Count > 0) return IssueOutcome.Fail(string.Join(" ", blocking));
            if (requireOfflineAllowed && !license.AllowOfflineActivation)
                return IssueOutcome.Fail("This license can only be activated online. Connect the computer to the internet and click Activate, or contact your vendor.");

            var terms = license.ToTerms(request.TierOverride, request.ExpiryOverride);
            var termsProblem = terms.Validate();
            if (termsProblem is not null) return IssueOutcome.Fail(termsProblem);

            var serialKey = SerialKey.Generate(terms, softCode.ActivationCode, fingerprint, _secrets.Current);

            license.RegisterActivation(new ActivationRecord
            {
                MachineFingerprint = fingerprint.Hex,
                SerialKeyIssued = serialKey,
                SoftCode = normalizedRequest,
                ActivationMode = mode,
                Status = ActivationStatus.Active,
                ActivationDate = UtcNow,
                IssuedTier = terms.Tier.ToString(),
                IssuedExpiryDate = terms.ExpiryDate,
                GeneratedByAdminUserId = mode == OfflineMode ? _currentUser.AdminUserId : null,
            }, UtcNow);
            await _unitOfWork.SaveChangesAsync(ct);

            return IssueOutcome.Ok(serialKey, terms, license.ActivationCode);
        }, cancellationToken);

        if (outcome.Error is not null) return Result<SerialKeyIssuedDto>.Failure(outcome.Error);

        var issued = outcome.Terms!;
        _logger.LogInformation("Serial key issued ({Mode}) by {AdminUserId}", mode, _currentUser.AdminUserId);
        await _audit.LogAsync("SerialKey.Issued", new
        {
            ActivationCode = ActivationCodeFormat.Display(outcome.ActivationCode!),
            Fingerprint = fingerprint.Hex,
            Mode = mode,
            Tier = issued.Tier.ToString(),
            issued.ExpiryDate,
            Overridden = request.TierOverride is not null || request.ExpiryOverride is not null,
        }, cancellationToken: cancellationToken);

        return Result<SerialKeyIssuedDto>.Success(new SerialKeyIssuedDto(
            outcome.SerialKey!, issued.Tier, issued.Type, issued.ExpiryDate, issued.MaxCardsPerPeriod, issued.LimitPeriod));
    }

    private async Task<SoftCodePreviewDto> BuildPreviewAsync(LicenseCode? license, SoftCode softCode, CancellationToken cancellationToken)
    {
        var fingerprintHex = softCode.Fingerprint.Hex;
        var displayCode = softCode.ActivationCode.ToString();

        if (license is null)
        {
            return new SoftCodePreviewDto(displayCode, fingerprintHex, softCode.RequestDate, null, null, null, null, null, null, null, null,
                0, 0, false, false, [new ActivationProblemDto(UnknownCodeMessage, true)]);
        }

        var blacklisted = await _blacklist.IsBlacklistedAsync(fingerprintHex, license.ActivationCode, cancellationToken);
        var problems = ActivationRules.Evaluate(license, fingerprintHex, blacklisted, softCode.RequestDate, Today)
            .Select(p => new ActivationProblemDto(p.Message, p.IsBlocking)).ToList();

        return new SoftCodePreviewDto(
            displayCode, fingerprintHex, softCode.RequestDate,
            license.Id, license.Status, license.Tier, license.LicenseType, license.ExpiryDate, license.MaxCardsPerPeriod, license.CardLimitPeriod,
            license.CustomerRecord?.OrganizationName, license.ActiveActivations.Count(), license.MaxActivations,
            ActivationRules.IsSameMachineActive(license, fingerprintHex), license.AllowOfflineActivation, problems);
    }

    private sealed record IssueOutcome(string? Error, string? SerialKey, SerialKeyTerms? Terms, string? ActivationCode)
    {
        public static IssueOutcome Fail(string error) => new(error, null, null, null);
        public static IssueOutcome Ok(string serialKey, SerialKeyTerms terms, string activationCode) => new(null, serialKey, terms, activationCode);
    }
}
