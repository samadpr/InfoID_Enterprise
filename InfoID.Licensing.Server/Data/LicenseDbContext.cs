using InfoID.Licensing.Server.Entities;
using Microsoft.EntityFrameworkCore;

namespace InfoID.Licensing.Server.Data;

/// <summary>
/// The vendor license database (Domain Model Module N) on PostgreSQL. Table and column
/// names keep the Domain Model's PascalCase (Npgsql quotes them), so the SRS document
/// and the database read the same.
/// </summary>
public class LicenseDbContext : DbContext
{
    /// <summary>Who is making changes, written into CreatedBy/ModifiedBy. Set by the
    /// app after login (the admin's email).</summary>
    public string? CurrentUser { get; set; }

    public LicenseDbContext(DbContextOptions<LicenseDbContext> options) : base(options) { }

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

    protected override void OnModelCreating(ModelBuilder b)
    {
        // Singular table names, as in the Domain Model.
        b.Entity<AdminUser>(e =>
        {
            e.ToTable("AdminUser");
            e.Property(x => x.FullName).HasMaxLength(200).IsRequired();
            e.Property(x => x.Email).HasMaxLength(200).IsRequired();
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.PasswordHash).HasMaxLength(300).IsRequired();
            e.Property(x => x.Role).HasMaxLength(30).IsRequired();
        });

        b.Entity<CustomerRecord>(e =>
        {
            e.ToTable("CustomerRecord");
            e.Property(x => x.OrganizationName).HasMaxLength(200).IsRequired();
            e.Property(x => x.OrganizationType).HasMaxLength(50);
            e.Property(x => x.Country).HasMaxLength(100);
            e.Property(x => x.ContactEmail).HasMaxLength(200);
            e.Property(x => x.ContactPhone).HasMaxLength(50);
            e.HasIndex(x => x.ContactEmail);
        });

        b.Entity<LicenseCode>(e =>
        {
            e.ToTable("LicenseCode");
            e.Property(x => x.ActivationCode).HasMaxLength(100).IsRequired();
            e.HasIndex(x => x.ActivationCode).IsUnique();
            e.Property(x => x.Tier).HasMaxLength(50).IsRequired();
            e.Property(x => x.LicenseType).HasMaxLength(30).IsRequired();
            e.Property(x => x.CardLimitPeriod).HasMaxLength(20).IsRequired();
            e.Property(x => x.Status).HasMaxLength(30).IsRequired();
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.BatchId);
            e.HasOne(x => x.CustomerRecord).WithMany(c => c.LicenseCodes).HasForeignKey(x => x.CustomerRecordId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.CreatedByAdminUser).WithMany().HasForeignKey(x => x.CreatedByAdminUserId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<ActivationRecord>(e =>
        {
            e.ToTable("ActivationRecord");
            e.Property(x => x.MachineFingerprint).HasMaxLength(500).IsRequired();
            e.HasIndex(x => x.MachineFingerprint);
            e.Property(x => x.SerialKeyIssued).HasMaxLength(500);
            e.Property(x => x.ActivationMode).HasMaxLength(20).IsRequired();
            e.Property(x => x.Status).HasMaxLength(30).IsRequired();
            e.Property(x => x.IssuedTier).HasMaxLength(50).IsRequired();
            e.Property(x => x.AppVersion).HasMaxLength(50);
            e.HasOne(x => x.LicenseCode).WithMany(c => c.Activations).HasForeignKey(x => x.LicenseCodeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.GeneratedByAdminUser).WithMany().HasForeignKey(x => x.GeneratedByAdminUserId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<TransferHistory>(e =>
        {
            e.ToTable("TransferHistory");
            e.Property(x => x.Reason).HasMaxLength(300);
            e.HasOne(x => x.LicenseCode).WithMany().HasForeignKey(x => x.LicenseCodeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.OldActivationRecord).WithMany().HasForeignKey(x => x.OldActivationRecordId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.NewActivationRecord).WithMany().HasForeignKey(x => x.NewActivationRecordId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<SupportTicket>(e =>
        {
            e.ToTable("SupportTicket");
            e.Property(x => x.Subject).HasMaxLength(250).IsRequired();
            e.Property(x => x.Status).HasMaxLength(30).IsRequired();
            e.HasOne(x => x.CustomerRecord).WithMany().HasForeignKey(x => x.CustomerRecordId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.LicenseCode).WithMany().HasForeignKey(x => x.LicenseCodeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.AssignedAdminUser).WithMany().HasForeignKey(x => x.AssignedAdminUserId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<AdminAuditLog>(e =>
        {
            e.ToTable("AdminAuditLog");
            e.Property(x => x.Action).HasMaxLength(150).IsRequired();
            e.Property(x => x.IPAddress).HasMaxLength(50);
            e.Property(x => x.PreviousHash).HasMaxLength(200);
            e.Property(x => x.RecordHash).HasMaxLength(200).IsRequired();
            e.HasIndex(x => x.ActionDate);
            e.HasOne(x => x.AdminUser).WithMany().HasForeignKey(x => x.AdminUserId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<FraudAlert>(e =>
        {
            e.ToTable("FraudAlert");
            e.Property(x => x.AlertType).HasMaxLength(50).IsRequired();
            e.Property(x => x.Severity).HasMaxLength(20).IsRequired();
            e.HasOne(x => x.ActivationRecord).WithMany().HasForeignKey(x => x.ActivationRecordId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<EmailTemplate>(e =>
        {
            e.ToTable("EmailTemplate");
            e.Property(x => x.Name).HasMaxLength(150).IsRequired();
            e.Property(x => x.Subject).HasMaxLength(300).IsRequired();
            e.Property(x => x.TemplateType).HasMaxLength(50).IsRequired();
        });

        b.Entity<BlacklistEntry>(e =>
        {
            e.ToTable("BlacklistEntry");
            e.Property(x => x.EntryType).HasMaxLength(30).IsRequired();
            e.Property(x => x.Value).HasMaxLength(500).IsRequired();
            e.Property(x => x.Reason).HasMaxLength(300);
            e.HasIndex(x => new { x.EntryType, x.Value });
        });

        // Common base fields on every table.
        foreach (var entity in b.Model.GetEntityTypes().Where(t => typeof(EntityBase).IsAssignableFrom(t.ClrType)).ToList())
        {
            b.Entity(entity.ClrType).Property(nameof(EntityBase.CreatedBy)).HasMaxLength(100);
            b.Entity(entity.ClrType).Property(nameof(EntityBase.ModifiedBy)).HasMaxLength(100);
            b.Entity(entity.ClrType).Property(nameof(EntityBase.Cancelled)).HasDefaultValue(false);
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
        foreach (var entry in ChangeTracker.Entries<EntityBase>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedDate = now;
                entry.Entity.ModifiedDate = now;
                entry.Entity.CreatedBy ??= CurrentUser;
                entry.Entity.ModifiedBy ??= CurrentUser;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.ModifiedDate = now;
                entry.Entity.ModifiedBy = CurrentUser;
            }
        }
    }
}
