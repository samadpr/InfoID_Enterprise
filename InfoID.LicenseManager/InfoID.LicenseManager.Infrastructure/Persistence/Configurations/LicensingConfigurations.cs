using InfoID.LicenseManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InfoID.LicenseManager.Infrastructure.Persistence.Configurations;

internal sealed class LicenseCodeConfiguration : IEntityTypeConfiguration<LicenseCode>
{
    public void Configure(EntityTypeBuilder<LicenseCode> e)
    {
        e.ToTable("LicenseCode");
        // Computed helpers of the domain entity, not columns or relationships.
        e.Ignore(x => x.ActiveActivations);
        e.Ignore(x => x.IsRevoked);
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
        e.Property(x => x.ProductCode).HasMaxLength(30).IsRequired().HasDefaultValue(ProductCodes.InfoID);
        e.HasIndex(x => x.ProductCode);
        e.HasOne(x => x.CustomerAccount).WithMany().HasForeignKey(x => x.CustomerAccountId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ActivationRecordConfiguration : IEntityTypeConfiguration<ActivationRecord>
{
    public void Configure(EntityTypeBuilder<ActivationRecord> e)
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
    }
}

internal sealed class TransferHistoryConfiguration : IEntityTypeConfiguration<TransferHistory>
{
    public void Configure(EntityTypeBuilder<TransferHistory> e)
    {
        e.ToTable("TransferHistory");
        e.Property(x => x.Reason).HasMaxLength(300);
        e.HasOne(x => x.LicenseCode).WithMany().HasForeignKey(x => x.LicenseCodeId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.OldActivationRecord).WithMany().HasForeignKey(x => x.OldActivationRecordId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.NewActivationRecord).WithMany().HasForeignKey(x => x.NewActivationRecordId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class BlacklistEntryConfiguration : IEntityTypeConfiguration<BlacklistEntry>
{
    public void Configure(EntityTypeBuilder<BlacklistEntry> e)
    {
        e.ToTable("BlacklistEntry");
        e.Property(x => x.EntryType).HasMaxLength(30).IsRequired();
        e.Property(x => x.Value).HasMaxLength(500).IsRequired();
        e.Property(x => x.Reason).HasMaxLength(300);
        e.HasIndex(x => new { x.EntryType, x.Value });
    }
}

internal sealed class FraudAlertConfiguration : IEntityTypeConfiguration<FraudAlert>
{
    public void Configure(EntityTypeBuilder<FraudAlert> e)
    {
        e.ToTable("FraudAlert");
        e.Property(x => x.AlertType).HasMaxLength(50).IsRequired();
        e.Property(x => x.Severity).HasMaxLength(20).IsRequired();
        e.HasOne(x => x.ActivationRecord).WithMany().HasForeignKey(x => x.ActivationRecordId).OnDelete(DeleteBehavior.Restrict);
    }
}
