using System.Reflection;
using InfoID.LicenseManager.Application.Common;
using InfoID.LicenseManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace InfoID.LicenseManager.Infrastructure.Persistence;

/// <summary>
/// The vendor license database (Domain Model Module N) on PostgreSQL. Table and column
/// names are exactly those of the existing schema created by InfoID.Licensing.Server's
/// migrations (kept in <c>Persistence/Migrations</c> with their original ids), so the
/// desktop License Manager, the License API and this web app can share one database.
/// </summary>
public class LicenseManagerDbContext : DbContext
{
    private readonly ICurrentUser? _currentUser;
    private string? _actorOverride;

    public LicenseManagerDbContext(DbContextOptions<LicenseManagerDbContext> options, ICurrentUser? currentUser = null)
        : base(options)
    {
        _currentUser = currentUser;
    }

    /// <summary>Who is making changes, written into CreatedBy/ModifiedBy. Defaults to the
    /// signed-in staff member; system actors (customer sign-up, license API...) set it.</summary>
    public string? CurrentUser
    {
        get => _actorOverride ?? _currentUser?.Email;
        set => _actorOverride = value;
    }

    public DbSet<AdminUser> AdminUsers => Set<AdminUser>();
    public DbSet<CustomerRecord> CustomerRecords => Set<CustomerRecord>();
    public DbSet<LicenseCode> LicenseCodes => Set<LicenseCode>();
    public DbSet<ActivationRecord> ActivationRecords => Set<ActivationRecord>();
    public DbSet<TransferHistory> TransferHistories => Set<TransferHistory>();
    public DbSet<SupportTicket> SupportTickets => Set<SupportTicket>();
    public DbSet<AdminAuditLog> AdminAuditLogs => Set<AdminAuditLog>();
    public DbSet<FraudAlert> FraudAlerts => Set<FraudAlert>();
    public DbSet<EmailTemplate> EmailTemplates => Set<EmailTemplate>();
    public DbSet<BlacklistEntry> BlacklistEntries => Set<BlacklistEntry>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<CustomerAccount> CustomerAccounts => Set<CustomerAccount>();
    public DbSet<EmailCode> EmailCodes => Set<EmailCode>();
    public DbSet<CustomerSession> CustomerSessions => Set<CustomerSession>();
    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<LicenseRequest> LicenseRequests => Set<LicenseRequest>();
    public DbSet<TrialUsage> TrialUsages => Set<TrialUsage>();
    public DbSet<TrialReservation> TrialReservations => Set<TrialReservation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        // Common base fields on every table.
        foreach (var entity in modelBuilder.Model.GetEntityTypes().Where(t => typeof(EntityBase).IsAssignableFrom(t.ClrType)).ToList())
        {
            modelBuilder.Entity(entity.ClrType).Property(nameof(EntityBase.CreatedBy)).HasMaxLength(100);
            modelBuilder.Entity(entity.ClrType).Property(nameof(EntityBase.ModifiedBy)).HasMaxLength(100);
            modelBuilder.Entity(entity.ClrType).Property(nameof(EntityBase.Cancelled)).HasDefaultValue(false);
        }
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        StampAuditFields();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        StampAuditFields();
        return base.SaveChanges();
    }

    private void StampAuditFields()
    {
        var now = DateTime.UtcNow;
        var actor = CurrentUser;
        foreach (var entry in ChangeTracker.Entries<EntityBase>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedDate = now;
                entry.Entity.ModifiedDate = now;
                entry.Entity.CreatedBy ??= actor;
                entry.Entity.ModifiedBy ??= actor;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.ModifiedDate = now;
                entry.Entity.ModifiedBy = actor;
            }
        }
    }
}
