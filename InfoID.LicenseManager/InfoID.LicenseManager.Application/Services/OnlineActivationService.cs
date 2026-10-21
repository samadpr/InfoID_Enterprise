using InfoID.Licensing.Core;
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

public sealed class OnlineActivationService : IOnlineActivationService
{
    public const string OnlineMode = "Online";
    private const string ActorName = "LicenseApi";

    /// <summary>How long an online license keeps working without a successful check-in. InfoID tries far more often.</summary>
    public static readonly TimeSpan CheckInWindow = TimeSpan.FromDays(60);

    private const string UnknownLicense = "This license is not known to the license server.";
    private const string InvalidRequest = "The request from InfoID was not valid.";

    private readonly ILicenseRepository _licenses;
    private readonly IBlacklistRepository _blacklist;
    private readonly IActivationRepository _activations;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILicenseTokenSigner _signer;
    private readonly IAuditService _audit;
    private readonly TimeProvider _time;
    private readonly ILogger<OnlineActivationService> _logger;

    public OnlineActivationService(ILicenseRepository licenses, IBlacklistRepository blacklist, IActivationRepository activations,
        IUnitOfWork unitOfWork, ILicenseTokenSigner signer, IAuditService audit, TimeProvider time, ILogger<OnlineActivationService> logger)
    {
        _licenses = licenses;
        _blacklist = blacklist;
        _activations = activations;
        _unitOfWork = unitOfWork;
        _signer = signer;
        _audit = audit;
        _time = time;
        _logger = logger;
    }

    private DateTime UtcNow => _time.GetUtcNow().UtcDateTime;

    public async Task<OnlineLicenseResultDto> ActivateAsync(string activationCode, string fingerprintHex, string? appVersion, CancellationToken cancellationToken = default)
    {
        if (!_signer.IsConfigured) return NotConfigured();
        if (!ActivationCode.TryParse(activationCode, out var code))
            return OnlineLicenseResultDto.Fail("That activation code isn't valid. Check it was typed exactly.");
        if (!TryParseFingerprint(fingerprintHex, out var fingerprint))
            return OnlineLicenseResultDto.Fail(InvalidRequest);

        _unitOfWork.UseActor(ActorName);
        var today = DateOnly.FromDateTime(UtcNow);

        var outcome = await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var license = await _licenses.FindForUpdateByActivationCodeAsync(code!.Value, ct);
            if (license is null) return Outcome.Refuse(["This activation code does not exist in the license database."]);

            var blacklisted = await _blacklist.IsBlacklistedAsync(fingerprint!.Hex, license.ActivationCode, ct);
            var problems = ActivationRules.Evaluate(license, fingerprint.Hex, blacklisted, today, today);
            if (problems.Count > 0) return Outcome.Refuse(problems.Select(p => p.Message).ToList());

            var terms = license.ToTerms();
            var activation = new ActivationRecord
            {
                MachineFingerprint = fingerprint.Hex,
                ActivationMode = OnlineMode,
                Status = ActivationStatus.Active,
                ActivationDate = UtcNow,
                LastValidationDate = UtcNow,
                IssuedTier = terms.Tier.ToString(),
                IssuedExpiryDate = terms.ExpiryDate,
                AppVersion = Truncate(appVersion, 50),
            };
            license.RegisterActivation(activation, UtcNow);
            await _unitOfWork.SaveChangesAsync(ct);

            return Outcome.Ok(Sign(activation.Id, license, fingerprint.Hex, terms), terms);
        }, cancellationToken);

        var display = code!.ToString();
        if (outcome.Problems is not null)
        {
            await _audit.LogAsync("Online.ActivationRefused", new { ActivationCode = display, Fingerprint = fingerprint!.Hex, Problems = outcome.Problems }, cancellationToken: cancellationToken);
            return OnlineLicenseResultDto.Fail(string.Join(" ", outcome.Problems));
        }

        await _audit.LogAsync("Online.Activated",
            new { ActivationCode = display, Fingerprint = fingerprint!.Hex, Tier = outcome.Terms!.Tier.ToString(), outcome.Terms.ExpiryDate, appVersion }, cancellationToken: cancellationToken);
        return new OnlineLicenseResultDto(true, outcome.Token, null, "Active");
    }

    public async Task<OnlineLicenseResultDto> CheckInAsync(string activationCode, string fingerprintHex, int? cardsIssued, string? appVersion, CancellationToken cancellationToken = default)
    {
        if (!_signer.IsConfigured) return NotConfigured();
        if (!ActivationCode.TryParse(activationCode, out var code) || !TryParseFingerprint(fingerprintHex, out var fingerprint))
            return OnlineLicenseResultDto.Fail(InvalidRequest, "Unknown");

        _unitOfWork.UseActor(ActorName);
        return await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var license = await _licenses.FindForUpdateByActivationCodeAsync(code!.Value, ct);
            if (license is null) return OnlineLicenseResultDto.Fail(UnknownLicense, "Unknown");
            if (license.IsRevoked) return OnlineLicenseResultDto.Fail("This license has been revoked. Please contact your vendor.", "Revoked");

            var activation = license.FindActiveActivation(fingerprint!.Hex);
            if (activation is null)
                return OnlineLicenseResultDto.Fail("This computer's license was deactivated. Activate again to continue.", "Deactivated");
            if (await _blacklist.IsBlacklistedAsync(fingerprint.Hex, license.ActivationCode, ct))
                return OnlineLicenseResultDto.Fail("This license is blocked. Please contact your vendor.", "Revoked");

            var terms = license.CheckInTerms(activation);
            activation.RecordCheckIn(UtcNow, terms.ExpiryDate, cardsIssued, Truncate(appVersion, 50));
            await _unitOfWork.SaveChangesAsync(ct);

            return new OnlineLicenseResultDto(true, Sign(activation.Id, license, fingerprint.Hex, terms), null, "Active");
        }, cancellationToken);
    }

    public async Task<Result> DeactivateAsync(string activationCode, string fingerprintHex, CancellationToken cancellationToken = default)
    {
        if (!ActivationCode.TryParse(activationCode, out var code) || !TryParseFingerprint(fingerprintHex, out var fingerprint))
            return Result.Failure(InvalidRequest);

        _unitOfWork.UseActor(ActorName);
        var freed = await _unitOfWork.ExecuteInTransactionAsync<int?>(async ct =>
        {
            var license = await _licenses.FindForUpdateByActivationCodeAsync(code!.Value, ct);
            if (license is null) return null;

            var history = license.DeactivateMachine(fingerprint!.Hex, UtcNow, "Deactivated from InfoID");
            _activations.AddTransfers(history);
            await _unitOfWork.SaveChangesAsync(ct);
            return history.Count;
        }, cancellationToken);

        if (freed is null) return Result.Failure(UnknownLicense);

        await _audit.LogAsync("Online.Deactivated", new { ActivationCode = code!.Value, Fingerprint = fingerprint!.Hex, Count = freed }, cancellationToken: cancellationToken);
        return Result.Success();
    }

    private string Sign(long licenseId, LicenseCode license, string fingerprintHex, SerialKeyTerms terms)
    {
        var now = UtcNow;
        return _signer.Sign(new OnlineLicense
        {
            LicenseId = licenseId,
            ActivationCode = license.ActivationCode,
            Fingerprint = fingerprintHex,
            Tier = terms.Tier,
            Type = terms.Type,
            ExpiryDate = terms.ExpiryDate,
            MaxCardsPerPeriod = terms.MaxCardsPerPeriod,
            LimitPeriod = terms.LimitPeriod,
            OrganizationName = license.CustomerRecord?.OrganizationName,
            IssuedUtc = now,
            CheckInDeadlineUtc = now.Add(CheckInWindow),
        });
    }

    private OnlineLicenseResultDto NotConfigured()
    {
        _logger.LogError("Online activation requested but no signing key is configured");
        return OnlineLicenseResultDto.Fail("The license server is not configured (no signing key).", "Error");
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

    private static string? Truncate(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Length > max ? value[..max] : value.Trim();

    private sealed record Outcome(IReadOnlyList<string>? Problems, string? Token, SerialKeyTerms? Terms)
    {
        public static Outcome Refuse(IReadOnlyList<string> problems) => new(problems, null, null);
        public static Outcome Ok(string token, SerialKeyTerms terms) => new(null, token, terms);
    }
}
