using InfoID.LicenseManager.Application.Common;
using InfoID.LicenseManager.Application.DTOs.Customers;
using InfoID.LicenseManager.Application.Interfaces.Repositories;
using InfoID.LicenseManager.Application.Interfaces.Services;
using InfoID.LicenseManager.Application.Validators;
using InfoID.LicenseManager.Domain.Entities;
using InfoID.Shared.Common;
using Microsoft.Extensions.Logging;

namespace InfoID.LicenseManager.Application.Services;

public sealed class CustomerAdminService : ICustomerAdminService
{
    private readonly ICustomerAccountRepository _accounts;
    private readonly IVerificationCodeService _verification;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditService _audit;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _time;
    private readonly ILogger<CustomerAdminService> _logger;

    public CustomerAdminService(ICustomerAccountRepository accounts, IVerificationCodeService verification, IUnitOfWork unitOfWork,
        IAuditService audit, ICurrentUser currentUser, TimeProvider time, ILogger<CustomerAdminService> logger)
    {
        _accounts = accounts;
        _verification = verification;
        _unitOfWork = unitOfWork;
        _audit = audit;
        _currentUser = currentUser;
        _time = time;
        _logger = logger;
    }

    public async Task<IReadOnlyList<CustomerListItemDto>> SearchAsync(CustomerSearchDto search, CancellationToken cancellationToken = default)
    {
        _currentUser.RequireStaff("view customers");
        var text = string.IsNullOrWhiteSpace(search.Text) ? null : search.Text.Trim();
        return await _accounts.SearchAsync(new CustomerSearchCriteria(
            Enum.IsDefined(search.Filter) ? search.Filter : CustomerFilter.All, text, Math.Clamp(search.Take, 1, 2000)), cancellationToken);
    }

    public async Task<CustomerDetailsDto> GetDetailsAsync(long accountId, CancellationToken cancellationToken = default)
    {
        _currentUser.RequireStaff("view customers");

        // Counts and status are derived from the loaded lists so the page cannot disagree with itself.
        var account = await _accounts.FindByIdAsync(accountId, cancellationToken) ?? throw new NotFoundException("Customer", accountId);
        var licenses = await _accounts.GetLicensesAsync(accountId, cancellationToken);
        var requests = await _accounts.GetRequestsAsync(accountId, cancellationToken);

        var item = new CustomerListItemDto(
            account.Id, account.FullName, account.CompanyName, account.Email, account.Phone, account.Country, account.CreatedDate,
            account.EmailVerified, account.LastLoginDate, account.IsDisabled, requests.Count, licenses.Count);
        return new CustomerDetailsDto(item, licenses, requests);
    }

    public async Task<Result> SetDisabledAsync(SetCustomerDisabledRequestDto request, CancellationToken cancellationToken = default)
    {
        _currentUser.RequireManageLicenses(request.Disabled ? "disable customer accounts" : "enable customer accounts");
        if (request.Disabled)
        {
            var reasonProblem = LicenseRequestValidators.ValidateReason(request.Reason, "disabling the account");
            if (reasonProblem is not null) return Result.Failure(reasonProblem);
        }

        var account = await _accounts.FindByIdAsync(request.AccountId, cancellationToken) ?? throw new NotFoundException("Customer", request.AccountId);
        if (account.IsDisabled == request.Disabled)
            return Result.Failure(request.Disabled ? "This account is already disabled." : "This account is already enabled.");

        if (request.Disabled)
        {
            account.Disable();
            // Sign out everywhere immediately.
            await _accounts.RevokeActiveSessionsAsync(account.Id, _time.GetUtcNow().UtcDateTime, cancellationToken);
        }
        else
        {
            account.Enable();
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken); // flag and revoked sessions commit together

        _logger.LogWarning("Staff {AdminUserId} {Action} customer account {AccountId}", _currentUser.AdminUserId, request.Disabled ? "disabled" : "enabled", account.Id);
        await _audit.LogAsync(request.Disabled ? "Customer.Disabled" : "Customer.Enabled",
            new { account.Email, Reason = request.Reason?.Trim() }, cancellationToken: cancellationToken);
        return Result.Success();
    }

    public async Task<Result> ResendVerificationAsync(long accountId, CancellationToken cancellationToken = default)
    {
        _currentUser.RequireStaff("resend verification codes");

        var account = await _accounts.FindByIdAsync(accountId, cancellationToken) ?? throw new NotFoundException("Customer", accountId);
        if (account.IsDisabled) return Result.Failure("This account is disabled. Enable it first.");
        if (account.EmailVerified) return Result.Failure("This account's e-mail is already verified.");

        var result = await _verification.IssueAsync(account, EmailCodePurpose.VerifyEmail, respectResendInterval: true, cancellationToken);
        switch (result)
        {
            case VerificationCodeResult.Throttled:
                return Result.Failure("A code was sent less than a minute ago. Please wait a minute before sending another.");
            case VerificationCodeResult.SendFailed:
                return Result.Failure("The code was created but the e-mail could not be sent. Check the mail settings and try again.");
        }

        await _audit.LogAsync("Customer.VerificationResent", new { account.Email }, cancellationToken: cancellationToken);
        return Result.Success();
    }
}
