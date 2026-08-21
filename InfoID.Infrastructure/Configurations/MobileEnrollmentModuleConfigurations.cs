using InfoID.Domain.Entities.MobileEnrollmentModule;
using InfoID.Domain.Entities.CardholderModule;
using InfoID.Domain.Entities.OrganizationModule;
using InfoID.Domain.Common.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InfoID.Infrastructure.Configurations;

public class MobileEnrollmentSessionConfiguration : IEntityTypeConfiguration<MobileEnrollmentSession>
{
    public void Configure(EntityTypeBuilder<MobileEnrollmentSession> builder)
    {
        builder.ToTable("MobileEnrollmentSession");

        builder.Property(x => x.SessionToken)
            .IsRequired()
            .HasMaxLength(200);
        builder.Property(x => x.QrCodeValue)
            .HasMaxLength(300);
        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.HasOne(x => x.Cardholder)
            .WithMany()
            .HasForeignKey(x => x.CardholderId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Branch)
            .WithMany()
            .HasForeignKey(x => x.BranchId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
