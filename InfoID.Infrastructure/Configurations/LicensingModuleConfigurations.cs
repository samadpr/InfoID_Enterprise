using InfoID.Domain.Entities.LicensingModule;
using InfoID.Domain.Entities.OrganizationModule;
using InfoID.Domain.Common.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InfoID.Infrastructure.Configurations;

public class LicenseRecordConfiguration : IEntityTypeConfiguration<LicenseRecord>
{
    public void Configure(EntityTypeBuilder<LicenseRecord> builder)
    {
        builder.ToTable("LicenseRecord");

        builder.Property(x => x.ActivationCode)
            .IsRequired()
            .HasMaxLength(100);
        builder.Property(x => x.SerialKey)
            .HasMaxLength(500);
        builder.Property(x => x.MachineFingerprint)
            .IsRequired()
            .HasMaxLength(500);
        builder.Property(x => x.LicenseTier)
            .HasConversion<string>()
            .HasMaxLength(30);
        builder.Property(x => x.LicenseType)
            .HasConversion<string>()
            .HasMaxLength(30);
        builder.Property(x => x.ActivationMode)
            .HasConversion<string>()
            .HasMaxLength(30);
        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.HasOne(x => x.Organization)
            .WithMany()
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class LicenseValidationLogConfiguration : IEntityTypeConfiguration<LicenseValidationLog>
{
    public void Configure(EntityTypeBuilder<LicenseValidationLog> builder)
    {
        builder.ToTable("LicenseValidationLog");

        builder.Property(x => x.ValidationResult)
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.HasOne(x => x.LicenseRecord)
            .WithMany()
            .HasForeignKey(x => x.LicenseRecordId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
