using InfoID.Domain.Entities.VisitorModule;
using InfoID.Domain.Entities.CardholderModule;
using InfoID.Domain.Entities.OrganizationModule;
using InfoID.Domain.Common.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InfoID.Infrastructure.Configurations;

/// <summary>Visitor registrations (badge workflow). Visitor code is unique; SourceKey is
/// indexed for the "already imported" check.</summary>
public class VisitorConfiguration : IEntityTypeConfiguration<Visitor>
{
    public void Configure(EntityTypeBuilder<Visitor> builder)
    {
        builder.ToTable("Visitor");

        builder.Property(x => x.VisitorCode).IsRequired().HasMaxLength(20);
        builder.HasIndex(x => x.VisitorCode).IsUnique();
        builder.Property(x => x.FullName).IsRequired().HasMaxLength(200);
        builder.Property(x => x.Phone).HasMaxLength(50);
        builder.Property(x => x.Email).HasMaxLength(200);
        builder.Property(x => x.Company).HasMaxLength(200);
        builder.Property(x => x.PersonToMeet).HasMaxLength(200);
        builder.Property(x => x.Purpose).HasMaxLength(500);
        builder.Property(x => x.PhotoPath).HasMaxLength(500);
        builder.Property(x => x.Source).HasConversion<string>().HasMaxLength(30);
        builder.Property(x => x.SourceName).HasMaxLength(260);
        builder.Property(x => x.SourceKey).HasMaxLength(300);
        builder.HasIndex(x => x.SourceKey);
        builder.HasIndex(x => x.VisitDate);
        builder.HasIndex(x => x.CreatedDate);
    }
}

public class VisitorBadgeConfiguration : IEntityTypeConfiguration<VisitorBadge>
{
    public void Configure(EntityTypeBuilder<VisitorBadge> builder)
    {
        builder.ToTable("VisitorBadge");


        builder.HasOne(x => x.Cardholder)
            .WithMany()
            .HasForeignKey(x => x.CardholderId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.HostAppUser)
            .WithMany()
            .HasForeignKey(x => x.HostAppUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class VisitorPreRegistrationConfiguration : IEntityTypeConfiguration<VisitorPreRegistration>
{
    public void Configure(EntityTypeBuilder<VisitorPreRegistration> builder)
    {
        builder.ToTable("VisitorPreRegistration");

        builder.Property(x => x.FullName)
            .IsRequired()
            .HasMaxLength(200);
        builder.Property(x => x.Email)
            .HasMaxLength(200);
        builder.Property(x => x.Phone)
            .HasMaxLength(50);
        builder.Property(x => x.ApprovalStatus)
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.HasOne(x => x.VisitorBadge)
            .WithMany()
            .HasForeignKey(x => x.VisitorBadgeId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.HostAppUser)
            .WithMany()
            .HasForeignKey(x => x.HostAppUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class VisitorCheckInLogConfiguration : IEntityTypeConfiguration<VisitorCheckInLog>
{
    public void Configure(EntityTypeBuilder<VisitorCheckInLog> builder)
    {
        builder.ToTable("VisitorCheckInLog");


        builder.HasOne(x => x.VisitorBadge)
            .WithMany()
            .HasForeignKey(x => x.VisitorBadgeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
