using InfoID.Licensing.Server.Data;
using InfoID.Licensing.Server.Entities;
using Microsoft.EntityFrameworkCore;

namespace InfoID.Licensing.Server.Services;

public sealed record CustomerRow(
    long Id,
    string FullName,
    string? CompanyName,
    string Email,
    string? Phone,
    string? Country,
    DateTime CreatedDate,
    bool EmailVerified,
    DateTime? LastLoginDate,
    bool Disabled,
    int RequestCount,
    int LicenseCount)
{
    /// <summary>Verified, active account with no requests or licenses yet: a lead for sales.</summary>
    public bool IsLead => EmailVerified && !Disabled && RequestCount == 0 && LicenseCount == 0;

    public string StatusText => Disabled ? "Disabled" : !EmailVerified ? "Not verified" : IsLead ? "Lead" : "Customer";
}

public static class CustomerFilters
{
    public const string All = "All";
    public const string Verified = "Verified";
    public const string NotVerified = "Not verified";
    public const string Leads = "Leads (no requests yet)";
    public const string Disabled = "Disabled";

    public static readonly string[] Values = [All, Leads, Verified, NotVerified, Disabled];
}

/// <summary>
/// License Manager > Customers: every customer account (sign-ups from the app), with
/// leads (signed up, nothing requested yet), their requests and licenses, and
/// disable/enable. Disabling uses the soft-delete flag (Cancelled): the account can't
/// log in, its sessions end, and its email stays reserved; enabling restores it.
/// </summary>
public sealed class CustomerAdminService
{
    private readonly IDbContextFactory<LicenseDbContext> _dbFactory;
    private readonly AdminContext _admin;
    private readonly AuditService _audit;

    public CustomerAdminService(IDbContextFactory<LicenseDbContext> dbFactory, AdminContext admin, AuditService audit)
    {
        _dbFactory = dbFactory;
        _admin = admin;
        _audit = audit;
    }

    public async Task<IReadOnlyList<CustomerRow>> SearchAsync(string? filter, string? text, int take = 1000)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var query = db.CustomerAccounts.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(text))
        {
            var t = text.Trim().ToLower();
            query = query.Where(a => a.FullName.ToLower().Contains(t) || a.Email.Contains(t) ||
                                     (a.CompanyName != null && a.CompanyName.ToLower().Contains(t)) ||
                                     (a.Phone != null && a.Phone.Contains(t)));
        }

        var rows = await query
            .OrderByDescending(a => a.CreatedDate)
            .Take(take)
            .Select(a => new CustomerRow(
                a.Id, a.FullName, a.CompanyName, a.Email, a.Phone, a.Country, a.CreatedDate,
                a.EmailVerified, a.LastLoginDate, a.Cancelled,
                db.LicenseRequests.Count(r => r.CustomerAccountId == a.Id && !r.Cancelled),
                db.LicenseCodes.Count(c => c.CustomerAccountId == a.Id && !c.Cancelled)))
            .ToListAsync();

        return filter switch
        {
            CustomerFilters.Verified => rows.Where(r => r.EmailVerified && !r.Disabled).ToList(),
            CustomerFilters.NotVerified => rows.Where(r => !r.EmailVerified && !r.Disabled).ToList(),
            CustomerFilters.Leads => rows.Where(r => r.IsLead).ToList(),
            CustomerFilters.Disabled => rows.Where(r => r.Disabled).ToList(),
            _ => rows,
        };
    }

    public async Task SetDisabledAsync(long accountId, bool disabled, string? reason)
    {
        _admin.Require(_admin.CanManageLicenses, disabled ? "disable customer accounts" : "enable customer accounts");
        if (disabled && string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Enter a reason for disabling the account.");

        await using var db = await _dbFactory.CreateDbContextAsync();
        db.CurrentUser = _admin.Email;
        var account = await db.CustomerAccounts.FirstAsync(a => a.Id == accountId);
        account.Cancelled = disabled;

        if (disabled)
        {
            // Sign out everywhere immediately.
            foreach (var session in await db.CustomerSessions.Where(s => s.CustomerAccountId == accountId && s.RevokedUtc == null).ToListAsync())
                session.RevokedUtc = DateTime.UtcNow;
        }
        await db.SaveChangesAsync();

        await _audit.LogAsync(disabled ? "Customer.Disabled" : "Customer.Enabled", new { account.Email, Reason = reason?.Trim() });
    }
}
