using InfoID.Domain.Entities.AccessControlModule;
using InfoID.Domain.Entities.IssuanceModule;
using InfoID.Domain.Entities.OrganizationModule;
using InfoID.Domain.Common.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InfoID.Infrastructure.Configurations;

public class AccessControlPlatformConfiguration : IEntityTypeConfiguration<AccessControlPlatform>
{
    public void Configure(EntityTypeBuilder<AccessControlPlatform> builder)
    {
        builder.ToTable("AccessControlPlatform");

        builder.Property(x => x.PlatformName)
            .HasConversion<string>()
            .HasMaxLength(30);
        builder.Property(x => x.ApiEndpoint)
            .HasMaxLength(300);
        builder.Property(x => x.SyncMode)
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.HasOne(x => x.Organization)
            .WithMany()
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class AccessControlSyncLogConfiguration : IEntityTypeConfiguration<AccessControlSyncLog>
{
    public void Configure(EntityTypeBuilder<AccessControlSyncLog> builder)
    {
        builder.ToTable("AccessControlSyncLog");

        builder.Property(x => x.Direction)
            .HasConversion<string>()
            .HasMaxLength(30);
        builder.Property(x => x.Result)
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.HasOne(x => x.AccessControlPlatform)
            .WithMany()
            .HasForeignKey(x => x.AccessControlPlatformId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Card)
            .WithMany()
            .HasForeignKey(x => x.CardId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
