using InfoID.Domain.Common.Interfaces;
using InfoID.Domain.Entities.OrganizationModule;
using InfoID.Domain.Entities.SystemModule;

namespace InfoID.Application.Services;

/// <summary>Shared by Print history and NFC encoding: the same organization / operator
/// rows and ONE card-number sequence (C-000123), whichever happens first.</summary>
internal static class IssuanceStore
{
    public const string LastCardNumberKey = "Issuance.LastCardNumber";

    /// <summary>The first organization row, created as "My Organization" if none exists.</summary>
    public static async Task<long> EnsureOrganizationAsync(IUnitOfWork uow, CancellationToken ct)
    {
        var orgs = uow.Repository<Organization>();
        var existing = (await orgs.GetAllAsync(ct)).OrderBy(o => o.CreatedDate).FirstOrDefault();
        if (existing is not null) return existing.Id;

        var org = new Organization { Name = "My Organization" };
        await orgs.AddAsync(org, ct);
        await uow.SaveChangesAsync(ct);
        return org.Id;
    }

    /// <summary>The single local operator profile (SRS 3.3).</summary>
    public static async Task<long> EnsureOperatorAsync(IUnitOfWork uow, long organizationId, CancellationToken ct)
    {
        var users = uow.Repository<AppUser>();
        var existing = (await users.GetAllAsync(ct)).OrderBy(u => u.CreatedDate).FirstOrDefault();
        if (existing is not null) return existing.Id;

        var user = new AppUser { OrganizationId = organizationId, FullName = "InfoID Operator" };
        await users.AddAsync(user, ct);
        await uow.SaveChangesAsync(ct);
        return user.Id;
    }

    public static async Task<long> GetLastCardNumberAsync(IUnitOfWork uow, CancellationToken ct)
    {
        var setting = await uow.Repository<SystemSetting>().SingleOrDefaultAsync(s => s.SettingKey == LastCardNumberKey && s.OrganizationId == null, ct);
        return long.TryParse(setting?.SettingValue, out var n) ? n : 0;
    }

    public static async Task SetLastCardNumberAsync(IUnitOfWork uow, long value, CancellationToken ct)
    {
        var repo = uow.Repository<SystemSetting>();
        var setting = await repo.SingleOrDefaultAsync(s => s.SettingKey == LastCardNumberKey && s.OrganizationId == null, ct);
        if (setting is null)
        {
            await repo.AddAsync(new SystemSetting { SettingKey = LastCardNumberKey, SettingValue = value.ToString() }, ct);
        }
        else
        {
            setting.SettingValue = value.ToString();
            setting.ModifiedDate = DateTime.UtcNow;
            repo.Update(setting);
        }
    }

    /// <summary>e.g. C-000123. Sequential per installation.</summary>
    public static string FormatCardNumber(long n) => $"C-{n:D6}";

    public static string Truncate(string value, int max) => value.Length > max ? value[..max] : value;
}
