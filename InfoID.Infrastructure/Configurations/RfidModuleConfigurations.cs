using InfoID.Domain.Entities.RfidModule;
using InfoID.Domain.Entities.IssuanceModule;
using InfoID.Domain.Entities.OrganizationModule;
using InfoID.Domain.Common.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InfoID.Infrastructure.Configurations;

public class RfidKeyProfileConfiguration : IEntityTypeConfiguration<RfidKeyProfile>
{
    public void Configure(EntityTypeBuilder<RfidKeyProfile> builder)
    {
        builder.ToTable("RfidKeyProfile");

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(150);
        builder.Property(x => x.CardTechnology)
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.HasOne(x => x.Organization)
            .WithMany()
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class RfidEncodeLogConfiguration : IEntityTypeConfiguration<RfidEncodeLog>
{
    public void Configure(EntityTypeBuilder<RfidEncodeLog> builder)
    {
        builder.ToTable("RfidEncodeLog");

        builder.Property(x => x.Operation)
            .HasConversion<string>()
            .HasMaxLength(30);
        builder.Property(x => x.Result)
            .HasConversion<string>()
            .HasMaxLength(30);
        builder.Property(x => x.FailureReason)
            .HasMaxLength(300);

        builder.HasOne(x => x.Card)
            .WithMany()
            .HasForeignKey(x => x.CardId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.RFIDKeyProfile)
            .WithMany()
            .HasForeignKey(x => x.RFIDKeyProfileId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
