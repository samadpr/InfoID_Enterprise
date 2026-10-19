using System;
using System.Threading.Tasks;
using InfoID.Desktop.Features.Account.Services;
using InfoID.Domain.Common.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace InfoID.Desktop.Core.Services;

/// <summary>
/// Records audit entries (FR-REP-4) from places in the Desktop app that don't get services
/// through DI (view models, file-based services created with <c>new</c>). Each call uses
/// its own DI scope; a failure to audit is logged and never interrupts the user.
/// Services that are created by DI should take <see cref="IAuditTrail"/> directly instead.
/// </summary>
public static class Audit
{
    private static readonly InfoID.Shared.Logging.AppLogger Log = InfoID.Shared.Logging.AppLog.For("InfoID.Audit");
    private static IServiceScopeFactory? _scopes;

    public static void Configure(IServiceProvider services) =>
        _scopes = services.GetRequiredService<IServiceScopeFactory>();

    /// <summary>Fire-and-forget (entries are still written in order by the audit trail).</summary>
    public static void Record(string module, string action, string description,
        string? entityName = null, long? entityId = null, object? before = null, object? after = null) =>
        _ = RecordAsync(new AuditEntry(module, action, description)
        {
            EntityName = entityName, EntityId = entityId, Before = before, After = after,
        });

    public static async Task RecordAsync(AuditEntry entry)
    {
        if (_scopes is null) return;
        try
        {
            using var scope = _scopes.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IAuditTrail>().RecordAsync(entry);
        }
        catch (Exception ex)
        {
            Log.Warn($"Couldn't record audit entry {entry.Module}/{entry.Action}", ex);
        }
    }
}

/// <summary>Who did it: the Windows user, plus the signed-in InfoID account if any,
/// e.g. "priya (priya@acme.com)".</summary>
public sealed class DesktopAuditActorProvider : IAuditActorProvider
{
    private readonly AccountSession _account;

    public DesktopAuditActorProvider(AccountSession account) => _account = account;

    public string CurrentActor
    {
        get
        {
            var email = _account.IsSignedIn ? _account.Profile?.Email : null;
            return string.IsNullOrWhiteSpace(email) ? Environment.UserName : $"{Environment.UserName} ({email})";
        }
    }
}
