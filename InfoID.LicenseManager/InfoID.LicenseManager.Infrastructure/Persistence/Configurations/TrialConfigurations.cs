using InfoID.LicenseManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InfoID.LicenseManager.Infrastructure.Persistence.Configurations;

internal sealed class TrialUsageConfiguration : IEntityTypeConfiguration<TrialUsage>
{
    public void Configure(EntityTypeBuilder<TrialUsage> e)
    {
        e.ToTable("TrialUsage", t =>
        {
            // Belt and braces: the trial service's conditional updates keep these in range.
            t.HasCheckConstraint("CK_TrialUsage_NonNegative", "\"CardsUsed\" >= 0 AND \"CardsReserved\" >= 0 AND \"CardLimit\" >= 0");
        });
        e.Property(x => x.ProductCode).HasMaxLength(30).IsRequired();
        e.HasIndex(x => new { x.CustomerAccountId, x.ProductCode }).IsUnique();
        e.HasOne(x => x.CustomerAccount).WithMany().HasForeignKey(x => x.CustomerAccountId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class TrialReservationConfiguration : IEntityTypeConfiguration<TrialReservation>
{
    public void Configure(EntityTypeBuilder<TrialReservation> e)
    {
        e.ToTable("TrialReservation");
        e.HasIndex(x => x.ReservationKey).IsUnique();
        e.Property(x => x.Status).HasMaxLength(20).IsRequired();
        e.HasIndex(x => new { x.TrialUsageId, x.Status });
        e.Property(x => x.DesignName).HasMaxLength(200);
        e.Property(x => x.MachineFingerprint).HasMaxLength(100);
        e.Property(x => x.AppVersion).HasMaxLength(50);
        e.HasOne(x => x.TrialUsage).WithMany().HasForeignKey(x => x.TrialUsageId).OnDelete(DeleteBehavior.Restrict);
    }
}
