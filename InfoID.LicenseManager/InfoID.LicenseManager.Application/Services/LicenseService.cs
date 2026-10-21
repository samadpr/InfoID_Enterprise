using InfoID.Licensing.Core.Codecs;
using InfoID.LicenseManager.Application.Common;
using InfoID.LicenseManager.Application.DTOs.Licenses;
using InfoID.LicenseManager.Application.Interfaces.Repositories;
using InfoID.LicenseManager.Application.Interfaces.Services;
using InfoID.LicenseManager.Application.Mappings;
using InfoID.LicenseManager.Application.Validators;
using InfoID.LicenseManager.Domain.Entities;
using InfoID.LicenseManager.Domain.Exceptions;
using InfoID.Shared.Common;
using Microsoft.Extensions.Logging;

namespace InfoID.LicenseManager.Application.Services;

public sealed class LicenseService : ILicenseService
{
    private static readonly string[] KnownStatuses = [LicenseCodeStatus.Available, LicenseCodeStatus.Active, LicenseCodeStatus.Revoked];

    private readonly ILicenseRepository _licenses;
    private readonly LicenseCodeFactory _factory;
    private readonly IActivationRepository _activations;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditService _audit;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _time;
    private readonly ILogger<LicenseService> _logger;

    public LicenseService(ILicenseRepository licenses, LicenseCodeFactory factory, IActivationRepository activations,
        IUnitOfWork unitOfWork, IAuditService audit, ICurrentUser currentUser, TimeProvider time, ILogger<LicenseService> logger)
    {
        _licenses = licenses;
        _factory = factory;
        _activations = activations;
        _unitOfWork = unitOfWork;
        _audit = audit;
        _currentUser = currentUser;
        _time = time;
        _logger = logger;
    }

    private DateTime UtcNow => _time.GetUtcNow().UtcDateTime;

    private DateOnly Today => DateOnly.FromDateTime(UtcNow);

    public async Task<Result<CreatedLicensesDto>> CreateAsync(CreateLicenseRequestDto request, CancellationToken cancellationToken = default)
    {
        _currentUser.RequireManageLicenses("create activation codes");

        var errors = LicenseRequestValidators.Validate(request, Today);
        if (errors.Count > 0) return Result<CreatedLicensesDto>.Failure(errors);

        BuiltLicenses built;
        try
        {
            // The product and the customer account are not part of the staff form: the server decides them.
            built = await _factory.BuildAsync(new NewLicenseSpec(
                request.Quantity, request.Tier, request.Type, request.ExpiryDate, request.MaxCardsPerPeriod, request.LimitPeriod,
                request.MaxActivations, request.AllowOfflineActivation, request.OrganizationName, request.OrganizationType,
                request.Country, request.ContactEmail, request.ContactPhone, request.Notes,
                ProductCodes.InfoID, CustomerAccountId: null, _currentUser.AdminUserId), cancellationToken);
        }
        catch (DomainRuleException ex)
        {
            return Result<CreatedLicensesDto>.Failure(ex.Message);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken); // codes and customer commit together or not at all

        _logger.LogInformation("Staff {AdminUserId} created {Count} activation code(s) (batch {BatchId})", _currentUser.AdminUserId, built.Codes.Count, built.BatchId);
        await _audit.LogAsync("LicenseCode.Created", new
        {
            Count = built.Codes.Count,
            built.BatchId,
            Tier = request.Tier.ToString(),
            Type = request.Type.ToString(),
            request.ExpiryDate,
            request.MaxCardsPerPeriod,
            Customer = built.Customer?.OrganizationName,
        }, cancellationToken: cancellationToken);

        return Result<CreatedLicensesDto>.Success(new CreatedLicensesDto(
            built.BatchId, built.Codes[0].Id, built.Codes.Select(c => ActivationCodeFormat.Display(c.ActivationCode)).ToList()));
    }

    public async Task<IReadOnlyList<LicenseListItemDto>> SearchAsync(LicenseSearchDto search, CancellationToken cancellationToken = default)
    {
        var status = string.IsNullOrWhiteSpace(search.Status) ? null : search.Status.Trim();
        if (status is not null && !KnownStatuses.Contains(status)) return [];

        var text = string.IsNullOrWhiteSpace(search.Text) ? null : search.Text.Trim();
        var codeFragment = text is null ? null : CrockfordBase32.Normalize(text);
        var criteria = new LicenseSearchCriteria(text, codeFragment, status, Math.Clamp(search.Take, 1, 1000), search.BatchId);

        var rows = await _licenses.SearchAsync(criteria, cancellationToken);
        return rows.Select(r => r with { ActivationCode = ActivationCodeFormat.Display(r.ActivationCode) }).ToList();
    }

    public async Task<LicenseDetailsDto> GetDetailsAsync(long licenseId, CancellationToken cancellationToken = default)
    {
        var license = await _licenses.FindDetailsAsync(licenseId, cancellationToken)
                      ?? throw new NotFoundException("License", licenseId);
        return license.ToDetailsDto();
    }

    public async Task<Result> RevokeAsync(RevokeLicenseRequestDto request, CancellationToken cancellationToken = default)
    {
        _currentUser.RequireManageLicenses("revoke licenses");
        var reasonProblem = LicenseRequestValidators.ValidateReason(request.Reason, "revoking");
        if (reasonProblem is not null) return Result.Failure(reasonProblem);

        var reason = request.Reason.Trim();
        var code = await ChangeLockedAsync(request.LicenseId, license =>
        {
            if (license.IsRevoked) throw new DomainRuleException("This license is already revoked.");
            license.Revoke(UtcNow);
        }, cancellationToken);
        if (code.IsFailure) return Result.Failure(code.Error!);

        _logger.LogWarning("Staff {AdminUserId} revoked license {LicenseId}", _currentUser.AdminUserId, request.LicenseId);
        await _audit.LogAsync("LicenseCode.Revoked", new { ActivationCode = code.Value, Reason = reason }, cancellationToken: cancellationToken);
        return Result.Success();
    }

    public async Task<Result> ExtendAsync(ExtendLicenseRequestDto request, CancellationToken cancellationToken = default)
    {
        _currentUser.RequireManageLicenses("extend licenses");

        DateOnly? oldExpiry = null;
        var code = await ChangeLockedAsync(request.LicenseId, license =>
        {
            oldExpiry = license.ExpiryDate;
            license.Extend(request.NewExpiryDate, Today);
        }, cancellationToken);
        if (code.IsFailure) return Result.Failure(code.Error!);

        _logger.LogInformation("Staff {AdminUserId} extended license {LicenseId} to {NewExpiry}", _currentUser.AdminUserId, request.LicenseId, request.NewExpiryDate);
        await _audit.LogAsync("LicenseCode.Extended",
            new { ActivationCode = code.Value, OldExpiry = oldExpiry, NewExpiry = request.NewExpiryDate }, cancellationToken: cancellationToken);
        return Result.Success();
    }

    public async Task<Result> SetOfflineActivationAsync(SetOfflineActivationRequestDto request, CancellationToken cancellationToken = default)
    {
        _currentUser.RequireManageLicenses("change offline activation");

        var code = await ChangeLockedAsync(request.LicenseId, license => license.SetAllowOfflineActivation(request.Allow), cancellationToken);
        if (code.IsFailure) return Result.Failure(code.Error!);

        await _audit.LogAsync(request.Allow ? "LicenseCode.OfflineAllowed" : "LicenseCode.OfflineDisallowed",
            new { ActivationCode = code.Value }, cancellationToken: cancellationToken);
        return Result.Success();
    }

    public async Task<Result<int>> FreeMachineSlotsAsync(FreeMachineSlotsRequestDto request, CancellationToken cancellationToken = default)
    {
        _currentUser.RequireManageLicenses("free machine slots");
        var reasonProblem = LicenseRequestValidators.ValidateReason(request.Reason, "freeing the machine slots");
        if (reasonProblem is not null) return Result<int>.Failure(reasonProblem);

        var reason = request.Reason.Trim();
        var freed = 0;
        var code = await ChangeLockedAsync(request.LicenseId, license =>
        {
            var history = license.FreeMachineSlots(UtcNow, reason);
            _activations.AddTransfers(history);
            freed = history.Count;
        }, cancellationToken);
        if (code.IsFailure) return Result<int>.Failure(code.Error!);

        _logger.LogWarning("Staff {AdminUserId} freed {Count} machine slot(s) of license {LicenseId}", _currentUser.AdminUserId, freed, request.LicenseId);
        await _audit.LogAsync("LicenseCode.SlotsFreed", new { ActivationCode = code.Value, Count = freed, Reason = reason }, cancellationToken: cancellationToken);
        return Result<int>.Success(freed);
    }

    /// <summary>Locks the code row, applies <paramref name="change"/> and saves, all in one transaction.
    /// A rule violation rolls the transaction back and comes back as a failure, never as an exception.</summary>
    private async Task<Outcome> ChangeLockedAsync(long licenseId, Action<LicenseCode> change, CancellationToken cancellationToken)
    {
        return await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var license = await _licenses.FindForUpdateAsync(licenseId, ct)
                          ?? throw new NotFoundException("License", licenseId);
            try
            {
                change(license);
            }
            catch (DomainRuleException ex)
            {
                return Outcome.Fail(ex.Message);
            }

            await _unitOfWork.SaveChangesAsync(ct);
            return Outcome.Ok(ActivationCodeFormat.Display(license.ActivationCode));
        }, cancellationToken);
    }

    private readonly record struct Outcome(bool Succeeded, string? Value, string? Error)
    {
        public bool IsFailure => !Succeeded;
        public static Outcome Ok(string value) => new(true, value, null);
        public static Outcome Fail(string error) => new(false, null, error);
    }
}
