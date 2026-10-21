using InfoID.LicenseManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InfoID.LicenseManager.Infrastructure.Persistence.Configurations;

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> e)
    {
        e.ToTable("Product");
        e.Property(x => x.Code).HasMaxLength(30).IsRequired();
        e.HasIndex(x => x.Code).IsUnique();
        e.Property(x => x.Name).HasMaxLength(150).IsRequired();
    }
}

internal sealed class PlanConfiguration : IEntityTypeConfiguration<Plan>
{
    public void Configure(EntityTypeBuilder<Plan> e)
    {
        e.ToTable("Plan");
        e.Property(x => x.ProductCode).HasMaxLength(30).IsRequired();
        e.Property(x => x.Name).HasMaxLength(150).IsRequired();
        e.Property(x => x.Tier).HasMaxLength(50).IsRequired();
        e.Property(x => x.Tagline).HasMaxLength(300);
        e.Property(x => x.CardLimitPeriod).HasMaxLength(20).IsRequired();
        e.Property(x => x.PriceText).HasMaxLength(100);
        e.HasIndex(x => new { x.ProductCode, x.SortOrder });
    }
}

internal sealed class LicenseRequestConfiguration : IEntityTypeConfiguration<LicenseRequest>
{
    public void Configure(EntityTypeBuilder<LicenseRequest> e)
    {
        e.ToTable("LicenseRequest");
        e.Property(x => x.ProductCode).HasMaxLength(30).IsRequired();
        e.Property(x => x.FullName).HasMaxLength(200).IsRequired();
        e.Property(x => x.CompanyName).HasMaxLength(200);
        e.Property(x => x.Email).HasMaxLength(200).IsRequired();
        e.Property(x => x.Phone).HasMaxLength(50);
        e.Property(x => x.Country).HasMaxLength(100);
        e.Property(x => x.StateProvince).HasMaxLength(100);
        e.Property(x => x.Message).HasMaxLength(2000);
        e.Property(x => x.Status).HasMaxLength(30).IsRequired();
        e.Property(x => x.StatusReason).HasMaxLength(500);
        e.Property(x => x.MachineFingerprint).HasMaxLength(100);
        e.Property(x => x.AppVersion).HasMaxLength(50);
        e.HasIndex(x => x.Status);
        e.HasIndex(x => x.Email);
        e.HasOne(x => x.Plan).WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.CustomerAccount).WithMany().HasForeignKey(x => x.CustomerAccountId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.HandledByAdminUser).WithMany().HasForeignKey(x => x.HandledByAdminUserId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.LicenseCode).WithMany().HasForeignKey(x => x.LicenseCodeId).OnDelete(DeleteBehavior.Restrict);
    }
}
