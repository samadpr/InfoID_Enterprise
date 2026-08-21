using InfoID.Domain.Common;
using InfoID.Domain.Entities.AccessControlModule;
using InfoID.Domain.Entities.ApprovalAuditModule;
using InfoID.Domain.Entities.CardholderModule;
using InfoID.Domain.Entities.DataImportModule;
using InfoID.Domain.Entities.IssuanceModule;
using InfoID.Domain.Entities.LicensingModule;
using InfoID.Domain.Entities.MobileEnrollmentModule;
using InfoID.Domain.Entities.OrganizationModule;
using InfoID.Domain.Entities.PrintingModule;
using InfoID.Domain.Entities.RfidModule;
using InfoID.Domain.Entities.SystemModule;
using InfoID.Domain.Entities.TemplateModule;
using InfoID.Domain.Entities.VisitorModule;
using Microsoft.EntityFrameworkCore;

namespace InfoID.Infrastructure;

/// <summary>
/// Embedded SQLite database via EF Core, auto-created at install (no external
/// DB server). Covers Modules A-M of the Domain Model (desktop app only).
/// Module N (License Management Admin Portal) is a separate database on a
/// separate solution/server -- see InfoID.AdminPortal.sln (not yet scaffolded).
/// </summary>
public class InfoIdDbContext : DbContext
{
    public InfoIdDbContext(DbContextOptions<InfoIdDbContext> options) : base(options)
    {
    }

        // --- Module A — Organization, User & Branch Setup ---
        public DbSet<Organization> Organizations => Set<Organization>();
        public DbSet<AppUser> AppUsers => Set<AppUser>();
        public DbSet<Branch> Branches => Set<Branch>();
        public DbSet<Department> Departments => Set<Department>();

        // --- Module B — Licensing (Local, Client-Side) ---
        public DbSet<LicenseRecord> LicenseRecords => Set<LicenseRecord>();
        public DbSet<LicenseValidationLog> LicenseValidationLogs => Set<LicenseValidationLog>();

        // --- Module C — Card Designer (Templates) ---
        public DbSet<Template> Templates => Set<Template>();
        public DbSet<TemplateElement> TemplateElements => Set<TemplateElement>();
        public DbSet<TemplateVersion> TemplateVersions => Set<TemplateVersion>();

        // --- Module D — Data Import & Management ---
        public DbSet<DataSourceConnection> DataSourceConnections => Set<DataSourceConnection>();
        public DbSet<FieldMappingProfile> FieldMappingProfiles => Set<FieldMappingProfile>();
        public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();
        public DbSet<ImportErrorLog> ImportErrorLogs => Set<ImportErrorLog>();

        // --- Module E — Cardholder Management ---
        public DbSet<Cardholder> Cardholders => Set<Cardholder>();
        public DbSet<CustomFieldDefinition> CustomFieldDefinitions => Set<CustomFieldDefinition>();
        public DbSet<CardholderCustomFieldValue> CardholderCustomFieldValues => Set<CardholderCustomFieldValue>();
        public DbSet<CardholderPhoto> CardholderPhotos => Set<CardholderPhoto>();
        public DbSet<CardholderSignature> CardholderSignatures => Set<CardholderSignature>();

        // --- Module F — Card Issuance & Lifecycle ---
        public DbSet<Card> Cards => Set<Card>();
        public DbSet<CardStatusHistory> CardStatusHistories => Set<CardStatusHistory>();

        // --- Module G — Printing Engine ---
        public DbSet<PrinterProfile> PrinterProfiles => Set<PrinterProfile>();
        public DbSet<PrintJob> PrintJobs => Set<PrintJob>();
        public DbSet<PrintJobItem> PrintJobItems => Set<PrintJobItem>();

        // --- Module H — RFID / Smart Card Encoding ---
        public DbSet<RfidKeyProfile> RfidKeyProfiles => Set<RfidKeyProfile>();
        public DbSet<RfidEncodeLog> RfidEncodeLogs => Set<RfidEncodeLog>();

        // --- Module I — Approval, Audit & Notifications ---
        public DbSet<ApprovalRecord> ApprovalRecords => Set<ApprovalRecord>();
        public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
        public DbSet<NotificationLog> NotificationLogs => Set<NotificationLog>();

        // --- Module J — Access Control Integration ---
        public DbSet<AccessControlPlatform> AccessControlPlatforms => Set<AccessControlPlatform>();
        public DbSet<AccessControlSyncLog> AccessControlSyncLogs => Set<AccessControlSyncLog>();

        // --- Module K — Visitor Management (Phase 3) ---
        public DbSet<VisitorBadge> VisitorBadges => Set<VisitorBadge>();
        public DbSet<VisitorPreRegistration> VisitorPreRegistrations => Set<VisitorPreRegistration>();
        public DbSet<VisitorCheckInLog> VisitorCheckInLogs => Set<VisitorCheckInLog>();

        // --- Module L — Mobile Enrollment (Phase 3) ---
        public DbSet<MobileEnrollmentSession> MobileEnrollmentSessions => Set<MobileEnrollmentSession>();

        // --- Module M — Reports, Plugins & System ---
        public DbSet<ReportDefinition> ReportDefinitions => Set<ReportDefinition>();
        public DbSet<PluginRegistration> PluginRegistrations => Set<PluginRegistration>();
        public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();
        public DbSet<BackupRestoreLog> BackupRestoreLogs => Set<BackupRestoreLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Picks up every IEntityTypeConfiguration<T> class in this assembly --
        // no need to register new entity configs by hand as the module count grows.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(InfoIdDbContext).Assembly);
    }

    /// <summary>
    /// Automatically stamps CreatedDate/ModifiedDate on save, so every module
    /// gets this for free instead of setting it manually in every service call.
    /// </summary>
    public override int SaveChanges()
    {
        ApplyAuditTimestamps();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyAuditTimestamps();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void ApplyAuditTimestamps()
    {
        var entries = ChangeTracker.Entries<BaseEntity>();

        foreach (var entry in entries)
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedDate = DateTime.UtcNow;
                    break;
                case EntityState.Modified:
                    entry.Entity.ModifiedDate = DateTime.UtcNow;
                    break;
            }
        }
    }
}
