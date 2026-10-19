using InfoID.Licensing.Core;
using InfoID.Licensing.Server.Data;
using InfoID.Licensing.Server.Entities;
using Microsoft.EntityFrameworkCore;

namespace InfoID.Licensing.Server.Services;

public sealed record SubmitRequestInput(
    long PlanId,
    int Machines,
    string FullName,
    string? CompanyName,
    string Email,
    string? Phone,
    string? Country,
    string? StateProvince,
    string? Message,
    string? MachineFingerprint,
    string? AppVersion);

public sealed record LicenseRequestRow(
    long Id,
    DateTime CreatedDate,
    string Status,
    string PlanName,
    string PlanTier,
    int? PlanDurationMonths,
    int Machines,
    string FullName,
    string? CompanyName,
    string Email,
    string? Phone,
    string? Country,
    string? StateProvince,
    string? Message,
    bool HasAccount,
    string? StatusReason,
    string? ActivationCode,
    string? HandledBy,
    DateTime? HandledDate,
    int? PlanMaxCards,
    string PlanLimitPeriod);

/// <summary>What staff confirm when approving (pre-filled from the plan and request).</summary>
public sealed record ApproveRequestInput(
    long RequestId,
    LicenseTier Tier,
    LicenseType Type,
    DateOnly? ExpiryDate,
    int? MaxCardsPerPeriod,
    CardLimitPeriod LimitPeriod,
    int MaxActivations,
    bool AllowOfflineActivation,
    string? Notes);

/// <summary>
/// Plan requests from the app: customers choose a plan and send their details (payment
/// happens outside the system); staff review them in License Manager and Approve, which
/// creates the activation code (linked to the customer's account, if any) and emails it.
/// </summary>
public sealed class LicenseRequestService
{
    private static readonly InfoID.Shared.Logging.AppLogger Log = InfoID.Shared.Logging.AppLog.For<LicenseRequestService>();

    private readonly IDbContextFactory<LicenseDbContext> _dbFactory;
    private readonly AdminContext _admin;
    private readonly AuditService _audit;
    private readonly IEmailSender _email;
    private readonly LicenseCodeService _codes;

    public LicenseRequestService(IDbContextFactory<LicenseDbContext> dbFactory, AdminContext admin, AuditService audit,
        IEmailSender email, LicenseCodeService codes)
    {
        _dbFactory = dbFactory;
        _admin = admin;
        _audit = audit;
        _email = email;
        _codes = codes;
    }

    // ================================================================ customer side ====

    /// <summary>Submit from the app. <paramref name="account"/> is the signed-in
    /// account (its email is used), or null for a request without an account.</summary>
    public async Task<(bool Succeeded, string? Error, long? RequestId)> SubmitAsync(SubmitRequestInput input, CustomerAccount? account)
    {
        var email = account?.Email ?? CustomerAccountService.NormalizeEmail(input.Email);
        if (string.IsNullOrWhiteSpace(input.FullName)) return (false, "Enter your name.", null);
        if (email is null) return (false, "Enter a valid email address.", null);
        if (input.Machines is < 1 or > 100) return (false, "Number of computers must be between 1 and 100.", null);

        await using var db = await _dbFactory.CreateDbContextAsync();
        db.CurrentUser = account?.Email ?? "CustomerRequest";
        var plan = await db.Plans.FirstOrDefaultAsync(p => p.Id == input.PlanId && p.IsActive && !p.Cancelled);
        if (plan is null) return (false, "That plan is no longer available. Please choose another.", null);

        // Basic flood protection: at most 5 open requests per email.
        var open = await db.LicenseRequests.CountAsync(r => r.Email == email && !r.Cancelled &&
            (r.Status == LicenseRequestStatus.New || r.Status == LicenseRequestStatus.InProgress));
        if (open >= 5) return (false, "You already have several requests in progress. We'll contact you soon.", null);

        var request = new LicenseRequest
        {
            ProductCode = plan.ProductCode,
            PlanId = plan.Id,
            CustomerAccountId = account?.Id,
            Machines = input.Machines,
            FullName = input.FullName.Trim(),
            CompanyName = Clean(input.CompanyName, 200),
            Email = email,
            Phone = Clean(input.Phone, 50),
            Country = Clean(input.Country, 100),
            StateProvince = Clean(input.StateProvince, 100),
            Message = Clean(input.Message, 2000),
            MachineFingerprint = Clean(input.MachineFingerprint, 100),
            AppVersion = Clean(input.AppVersion, 50),
        };
        db.LicenseRequests.Add(request);
        await db.SaveChangesAsync();
        await _audit.LogAsync("Request.Submitted", new { request.Id, Plan = plan.Name, request.Email, request.Machines });

        await TrySendAsync(email, $"We received your request: {plan.Name}",
            $"Hello {request.FullName},\n\nThank you for your request for {plan.Name} ({request.Machines} computer(s)). " +
            "Our team will contact you about payment. Once it's confirmed, your activation code will be emailed to you" +
            (account is not null ? " and will appear under \"My licenses\" in the app." : ".") +
            $"\n\nRequest number: {request.Id}");

        var notify = Environment.GetEnvironmentVariable("INFOID_SALES_NOTIFY_EMAIL");
        if (!string.IsNullOrWhiteSpace(notify))
            await TrySendAsync(notify, $"New license request #{request.Id}: {plan.Name}",
                $"Plan: {plan.Name}\nComputers: {request.Machines}\nName: {request.FullName}\nCompany: {request.CompanyName}\n" +
                $"Email: {request.Email}\nPhone: {request.Phone}\nCountry: {request.Country} {request.StateProvince}\n" +
                $"Account: {(account is not null ? "yes" : "no")}\nMessage: {request.Message}\n\nOpen License Manager > License Requests to process it.");

        return (true, null, request.Id);
    }

    // ================================================================ staff side ====

    public async Task<IReadOnlyList<LicenseRequestRow>> SearchAsync(string? status, string? text, int take = 500)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var query = db.LicenseRequests.AsNoTracking()
            .Include(r => r.Plan).Include(r => r.LicenseCode).Include(r => r.HandledByAdminUser)
            .Where(r => !r.Cancelled);

        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(r => r.Status == status);
        if (!string.IsNullOrWhiteSpace(text))
        {
            var t = text.Trim().ToLower();
            query = query.Where(r => r.FullName.ToLower().Contains(t) || r.Email.Contains(t) ||
                                     (r.CompanyName != null && r.CompanyName.ToLower().Contains(t)) ||
                                     (r.Phone != null && r.Phone.Contains(t)));
        }

        var rows = await query.OrderByDescending(r => r.CreatedDate).Take(take).ToListAsync();
        return rows.Select(r => new LicenseRequestRow(
            r.Id, r.CreatedDate, r.Status, r.Plan?.Name ?? "", r.Plan?.Tier ?? nameof(LicenseTier.Standard), r.Plan?.DurationMonths,
            r.Machines, r.FullName, r.CompanyName, r.Email, r.Phone, r.Country, r.StateProvince, r.Message,
            r.CustomerAccountId is not null, r.StatusReason,
            r.LicenseCode is { } c && ActivationCode.TryParse(c.ActivationCode, out var code) ? code!.ToString() : null,
            r.HandledByAdminUser?.FullName, r.HandledDate,
            r.Plan?.MaxCardsPerPeriod, r.Plan?.CardLimitPeriod ?? nameof(CardLimitPeriod.Monthly))).ToList();
    }

    public async Task<int> CountNewAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.LicenseRequests.CountAsync(r => r.Status == LicenseRequestStatus.New && !r.Cancelled);
    }

    public async Task MarkInProgressAsync(long requestId)
    {
        _admin.Require(_admin.CanManageLicenses, "process license requests");
        await using var db = await _dbFactory.CreateDbContextAsync();
        db.CurrentUser = _admin.Email;
        var request = await db.LicenseRequests.FirstAsync(r => r.Id == requestId);
        EnsureOpen(request);
        request.Status = LicenseRequestStatus.InProgress;
        request.HandledByAdminUserId = _admin.CurrentAdmin?.Id;
        request.HandledDate = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await _audit.LogAsync("Request.InProgress", new { request.Id });
    }

    /// <summary>Creates the activation code from the confirmed terms, links it to the
    /// request and the customer's account, and emails it. Returns the code.</summary>
    public async Task<string> ApproveAsync(ApproveRequestInput input)
    {
        _admin.Require(_admin.CanManageLicenses, "approve license requests");

        LicenseRequest request;
        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            request = await db.LicenseRequests.AsNoTracking().FirstAsync(r => r.Id == input.RequestId);
        }
        EnsureOpen(request);

        var codes = await _codes.CreateAsync(new CreateLicenseCodesRequest(
            1, input.Tier, input.Type, input.ExpiryDate, input.MaxCardsPerPeriod, input.LimitPeriod, input.MaxActivations,
            request.CompanyName ?? request.FullName, null, request.Country, request.Email, request.Phone,
            string.IsNullOrWhiteSpace(input.Notes) ? $"From request #{request.Id}" : $"From request #{request.Id}. {input.Notes.Trim()}",
            input.AllowOfflineActivation, request.CustomerAccountId, request.ProductCode));
        var formatted = codes[0];

        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            db.CurrentUser = _admin.Email;
            var normalized = ActivationCode.TryParse(formatted, out var parsed) ? parsed!.Value : formatted;
            var code = await db.LicenseCodes.FirstAsync(c => c.ActivationCode == normalized);
            var entity = await db.LicenseRequests.FirstAsync(r => r.Id == input.RequestId);
            entity.Status = LicenseRequestStatus.Approved;
            entity.LicenseCodeId = code.Id;
            entity.HandledByAdminUserId = _admin.CurrentAdmin?.Id;
            entity.HandledDate = DateTime.UtcNow;
            entity.StatusReason = null;

            if (entity.CustomerAccountId is { } accountId)
            {
                var account = await db.CustomerAccounts.FirstAsync(a => a.Id == accountId);
                account.CustomerRecordId ??= code.CustomerRecordId;
            }
            await db.SaveChangesAsync();
        }

        await _audit.LogAsync("Request.Approved", new { request.Id, ActivationCode = formatted, input.Tier, input.ExpiryDate });

        await TrySendAsync(request.Email, "Your activation code",
            $"Hello {request.FullName},\n\nYour license is ready.\n\nActivation code: {formatted}\n" +
            $"Edition: {input.Tier}\n" +
            (input.ExpiryDate is { } e ? $"Valid until: {e:dd MMM yyyy}\n" : "Valid: perpetual\n") +
            $"Computers: {input.MaxActivations}\n\n" +
            "To activate: open the app, go to Help > License, enter the code and click Activate" +
            (request.CustomerAccountId is not null ? " (or sign in and click Activate under \"My licenses\")." : "."));

        return formatted;
    }

    public async Task RejectAsync(long requestId, string reason)
    {
        _admin.Require(_admin.CanManageLicenses, "reject license requests");
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Enter a reason (the customer sees it).");

        await using var db = await _dbFactory.CreateDbContextAsync();
        db.CurrentUser = _admin.Email;
        var request = await db.LicenseRequests.FirstAsync(r => r.Id == requestId);
        EnsureOpen(request);
        request.Status = LicenseRequestStatus.Rejected;
        request.StatusReason = reason.Trim();
        request.HandledByAdminUserId = _admin.CurrentAdmin?.Id;
        request.HandledDate = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await _audit.LogAsync("Request.Rejected", new { request.Id, Reason = request.StatusReason });

        await TrySendAsync(request.Email, "About your license request",
            $"Hello {request.FullName},\n\nWe couldn't complete your request #{request.Id}.\nReason: {request.StatusReason}\n\nPlease contact us if you have any questions.");
    }

    private static void EnsureOpen(LicenseRequest request)
    {
        if (request.Status is LicenseRequestStatus.Approved or LicenseRequestStatus.Rejected)
            throw new InvalidOperationException($"This request is already {request.Status.ToLowerInvariant()}.");
    }

    private async Task TrySendAsync(string to, string subject, string body)
    {
        try
        {
            await _email.SendAsync(to, subject, body);
        }
        catch (Exception ex)
        {
            Log.Error($"Email \"{subject}\" to {InfoID.Shared.Logging.LogText.MaskEmail(to)} could not be sent", ex);
            await _audit.LogAsync("Email.Failed", new { To = to, Subject = subject, Error = ex.GetBaseException().Message });
        }
    }

    private static string? Clean(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length > max ? trimmed[..max] : trimmed;
    }
}
