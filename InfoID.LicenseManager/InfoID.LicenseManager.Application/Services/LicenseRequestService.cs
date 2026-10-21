using InfoID.LicenseManager.Application.Common;
using InfoID.LicenseManager.Application.DTOs.Accounts;
using InfoID.LicenseManager.Application.DTOs.Requests;
using InfoID.LicenseManager.Application.Interfaces.Repositories;
using InfoID.LicenseManager.Application.Interfaces.Services;
using InfoID.LicenseManager.Application.Mappings;
using InfoID.LicenseManager.Application.Validators;
using InfoID.LicenseManager.Domain.Entities;
using InfoID.LicenseManager.Domain.Exceptions;
using InfoID.Shared.Common;
using Microsoft.Extensions.Logging;

namespace InfoID.LicenseManager.Application.Services;

public sealed class LicenseRequestService : ILicenseRequestService
{
    private readonly ILicenseRequestRepository _requests;
    private readonly ICustomerAccountRepository _accounts;
    private readonly IPlanRepository _plans;
    private readonly SalesNotificationOptions _sales;
    private readonly LicenseCodeFactory _factory;
    private readonly CustomerMailer _mailer;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditService _audit;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _time;
    private readonly ILogger<LicenseRequestService> _logger;

    public LicenseRequestService(ILicenseRequestRepository requests, ICustomerAccountRepository accounts, IPlanRepository plans, SalesNotificationOptions sales, LicenseCodeFactory factory,
        CustomerMailer mailer, IUnitOfWork unitOfWork, IAuditService audit, ICurrentUser currentUser, TimeProvider time,
        ILogger<LicenseRequestService> logger)
    {
        _requests = requests;
        _accounts = accounts;
        _plans = plans;
        _sales = sales;
        _factory = factory;
        _mailer = mailer;
        _unitOfWork = unitOfWork;
        _audit = audit;
        _currentUser = currentUser;
        _time = time;
        _logger = logger;
    }

    private DateTime UtcNow => _time.GetUtcNow().UtcDateTime;

    public async Task<IReadOnlyList<LicenseRequestDto>> SearchAsync(LicenseRequestSearchDto search, CancellationToken cancellationToken = default)
    {
        _currentUser.RequireStaff("view license requests");

        IReadOnlyList<string>? statuses = (search.Status?.Trim()) switch
        {
            null or "" or RequestStatusFilter.Open => [LicenseRequestStatus.New, LicenseRequestStatus.InProgress],
            RequestStatusFilter.All => null,
            LicenseRequestStatus.New or LicenseRequestStatus.InProgress or LicenseRequestStatus.Approved or LicenseRequestStatus.Rejected
                => [search.Status!.Trim()],
            _ => [], // an unknown filter matches nothing instead of silently showing everything
        };
        if (statuses is { Count: 0 }) return [];

        var text = string.IsNullOrWhiteSpace(search.Text) ? null : search.Text.Trim();
        var rows = await _requests.SearchAsync(new LicenseRequestSearchCriteria(statuses, text, Math.Clamp(search.Take, 1, 1000)), cancellationToken);
        return rows.Select(r => r.ToDto()).ToList();
    }

    public async Task<LicenseRequestDto> GetAsync(long requestId, CancellationToken cancellationToken = default)
    {
        _currentUser.RequireStaff("view license requests");
        var request = await _requests.FindDetailsAsync(requestId, cancellationToken) ?? throw new NotFoundException("Request", requestId);
        return request.ToDto();
    }

    public Task<int> CountNewAsync(CancellationToken cancellationToken = default)
    {
        _currentUser.RequireStaff("view license requests");
        return _requests.CountNewAsync(cancellationToken);
    }

    public async Task<Result> MarkInProgressAsync(long requestId, CancellationToken cancellationToken = default)
    {
        _currentUser.RequireManageLicenses("process license requests");

        var outcome = await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var request = await _requests.FindForUpdateAsync(requestId, ct) ?? throw new NotFoundException("Request", requestId);
            try
            {
                request.MarkInProgress(_currentUser.AdminUserId, UtcNow);
            }
            catch (DomainRuleException ex)
            {
                return Result.Failure(ex.Message);
            }

            await _unitOfWork.SaveChangesAsync(ct);
            return Result.Success();
        }, cancellationToken);
        if (!outcome.Succeeded) return outcome;

        await _audit.LogAsync("Request.InProgress", new { Id = requestId }, cancellationToken: cancellationToken);
        return outcome;
    }

    public async Task<Result<ApprovedLicenseRequestDto>> ApproveAsync(ApproveLicenseRequestDto input, CancellationToken cancellationToken = default)
    {
        _currentUser.RequireManageLicenses("approve license requests");

        var errors = LicenseRequestValidators.Validate(input, DateOnly.FromDateTime(UtcNow));
        if (errors.Count > 0) return Result<ApprovedLicenseRequestDto>.Failure(errors);

        // One transaction under a lock on the request: the code, the request's new state and the account link
        // commit together. Two staff approving the same request queue up; the second sees it already approved.
        var outcome = await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var request = await _requests.FindForUpdateAsync(input.RequestId, ct) ?? throw new NotFoundException("Request", input.RequestId);
            try
            {
                request.EnsureOpen();

                var built = await _factory.BuildAsync(new NewLicenseSpec(
                    1, input.Tier, input.Type, input.ExpiryDate, input.MaxCardsPerPeriod, input.LimitPeriod, input.MaxActivations,
                    input.AllowOfflineActivation,
                    request.CompanyName ?? request.FullName, null, request.Country, request.Email, request.Phone,
                    string.IsNullOrWhiteSpace(input.Notes) ? $"From request #{request.Id}" : $"From request #{request.Id}. {input.Notes.Trim()}",
                    request.ProductCode, request.CustomerAccountId, _currentUser.AdminUserId), ct);
                var license = built.Codes[0];

                request.Approve(license, _currentUser.AdminUserId, UtcNow);

                if (request.CustomerAccountId is { } accountId
                    && await _accounts.FindByIdAsync(accountId, ct) is { CustomerRecordId: null, CustomerRecord: null } account)
                {
                    account.CustomerRecord = license.CustomerRecord; // first approval links the account to the vendor-side organisation
                }

                await _unitOfWork.SaveChangesAsync(ct);
                return new ApprovalOutcome(null, license, request, built.Customer?.OrganizationName);
            }
            catch (DomainRuleException ex)
            {
                return new ApprovalOutcome(ex.Message, null, request, null);
            }
        }, cancellationToken);

        if (outcome.Error is not null) return Result<ApprovedLicenseRequestDto>.Failure(outcome.Error);

        var request = outcome.Request;
        var displayCode = ActivationCodeFormat.Display(outcome.License!.ActivationCode);
        _logger.LogInformation("Staff {AdminUserId} approved license request {RequestId}", _currentUser.AdminUserId, request.Id);
        await _audit.LogAsync("LicenseCode.Created", new
        {
            Count = 1,
            BatchId = (Guid?)null,
            Tier = input.Tier.ToString(),
            Type = input.Type.ToString(),
            input.ExpiryDate,
            input.MaxCardsPerPeriod,
            Customer = outcome.Customer,
        }, cancellationToken: cancellationToken);
        await _audit.LogAsync("Request.Approved", new { request.Id, ActivationCode = displayCode, input.Tier, input.ExpiryDate }, cancellationToken: cancellationToken);

        var sent = await _mailer.TrySendAsync(request.Email, "Your activation code",
            $"Hello {request.FullName},\n\nYour license is ready.\n\nActivation code: {displayCode}\n" +
            $"Edition: {input.Tier}\n" +
            (input.ExpiryDate is { } e ? $"Valid until: {e:dd MMM yyyy}\n" : "Valid: perpetual\n") +
            $"Computers: {input.MaxActivations}\n\n" +
            "To activate: open the app, go to Help > License, enter the code and click Activate" +
            (request.CustomerAccountId is not null ? " (or sign in and click Activate under \"My licenses\")." : "."),
            "Email.Failed", error => new { To = request.Email, Subject = "Your activation code", Error = error }, cancellationToken);

        return Result<ApprovedLicenseRequestDto>.Success(new ApprovedLicenseRequestDto(displayCode, sent));
    }

    public async Task<Result> RejectAsync(RejectLicenseRequestDto input, CancellationToken cancellationToken = default)
    {
        _currentUser.RequireManageLicenses("reject license requests");
        var reasonProblem = LicenseRequestValidators.ValidateReason(input.Reason, "rejecting (the customer sees it)");
        if (reasonProblem is not null) return Result.Failure(reasonProblem);

        var outcome = await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var request = await _requests.FindForUpdateAsync(input.RequestId, ct) ?? throw new NotFoundException("Request", input.RequestId);
            try
            {
                request.Reject(input.Reason, _currentUser.AdminUserId, UtcNow);
            }
            catch (DomainRuleException ex)
            {
                return (Error: ex.Message, request);
            }

            await _unitOfWork.SaveChangesAsync(ct);
            return (Error: (string?)null, request);
        }, cancellationToken);
        if (outcome.Error is not null) return Result.Failure(outcome.Error);

        var request = outcome.request;
        _logger.LogInformation("Staff {AdminUserId} rejected license request {RequestId}", _currentUser.AdminUserId, request.Id);
        await _audit.LogAsync("Request.Rejected", new { request.Id, Reason = request.StatusReason }, cancellationToken: cancellationToken);

        await _mailer.TrySendAsync(request.Email, "About your license request",
            $"Hello {request.FullName},\n\nWe couldn't complete your request #{request.Id}.\nReason: {request.StatusReason}\n\nPlease contact us if you have any questions.",
            "Email.Failed", error => new { To = request.Email, Subject = "About your license request", Error = error }, cancellationToken);
        return Result.Success();
    }

    public async Task<SubmittedRequestDto> SubmitAsync(SubmitLicenseRequestDto input, AccountSessionDto? signedInAccount, CancellationToken cancellationToken = default)
    {
        // A signed-in, verified account always uses its own e-mail; otherwise the typed one must look valid.
        var email = signedInAccount?.Profile.Email ?? EmailAddressNormalizer.Normalize(input.Email);
        if (string.IsNullOrWhiteSpace(input.FullName)) return new SubmittedRequestDto(false, "Enter your name.", null);
        if (email is null) return new SubmittedRequestDto(false, "Enter a valid email address.", null);
        if (input.Machines is < 1 or > LicenseCode.MaxMachinesPerCode)
            return new SubmittedRequestDto(false, "Number of computers must be between 1 and 100.", null);

        _unitOfWork.UseActor(signedInAccount?.Profile.Email ?? "CustomerRequest");
        var plan = await _plans.FindActiveAsync(input.PlanId, cancellationToken);
        if (plan is null) return new SubmittedRequestDto(false, "That plan is no longer available. Please choose another.", null);

        // Basic flood protection: at most 5 open requests per e-mail.
        if (await _requests.CountOpenByEmailAsync(email, cancellationToken) >= LicenseRequest.MaxOpenRequestsPerEmail)
            return new SubmittedRequestDto(false, "You already have several requests in progress. We'll contact you soon.", null);

        var request = LicenseRequest.Submit(plan, input.Machines, input.FullName, email, signedInAccount?.AccountId, input.CompanyName,
            input.Phone, input.Country, input.StateProvince, input.Message, input.MachineFingerprint, input.AppVersion);
        _requests.Add(request);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _audit.LogAsync("Request.Submitted", new { request.Id, Plan = plan.Name, request.Email, request.Machines }, cancellationToken: cancellationToken);

        await _mailer.TrySendAsync(email, $"We received your request: {plan.Name}",
            $"Hello {request.FullName},\n\nThank you for your request for {plan.Name} ({request.Machines} computer(s)). " +
            "Our team will contact you about payment. Once it's confirmed, your activation code will be emailed to you" +
            (signedInAccount is not null ? " and will appear under \"My licenses\" in the app." : ".") +
            $"\n\nRequest number: {request.Id}",
            "Email.Failed", error => new { To = email, Subject = "Request received", Error = error }, cancellationToken);

        if (!string.IsNullOrWhiteSpace(_sales.Email))
            await _mailer.TrySendAsync(_sales.Email, $"New license request #{request.Id}: {plan.Name}",
                $"Plan: {plan.Name}\nComputers: {request.Machines}\nName: {request.FullName}\nCompany: {request.CompanyName}\n" +
                $"Email: {request.Email}\nPhone: {request.Phone}\nCountry: {request.Country} {request.StateProvince}\n" +
                $"Account: {(signedInAccount is not null ? "yes" : "no")}\nMessage: {request.Message}\n\nOpen License Manager > License requests to process it.",
                "Email.Failed", error => new { To = _sales.Email, Subject = "New request notification", Error = error }, cancellationToken);

        return new SubmittedRequestDto(true, null, request.Id);
    }

    private sealed record ApprovalOutcome(string? Error, LicenseCode? License, LicenseRequest Request, string? Customer);
}
