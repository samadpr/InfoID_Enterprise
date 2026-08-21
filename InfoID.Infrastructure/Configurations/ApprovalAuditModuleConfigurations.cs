using InfoID.Domain.Entities.ApprovalAuditModule;
using InfoID.Domain.Entities.OrganizationModule;
using InfoID.Domain.Common.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InfoID.Infrastructure.Configurations;

public class ApprovalRecordConfiguration : IEntityTypeConfiguration<ApprovalRecord>
{
    public void Configure(EntityTypeBuilder<ApprovalRecord> builder)
    {
        builder.ToTable("ApprovalRecord");

        builder.Property(x => x.Context)
            .IsRequired()
            .HasMaxLength(50);

        builder.HasOne(x => x.ApprovedByUser)
            .WithMany()
            .HasForeignKey(x => x.ApprovedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLog");

        builder.Property(x => x.Module)
            .IsRequired()
            .HasMaxLength(50);
        builder.Property(x => x.Action)
            .IsRequired()
            .HasMaxLength(100);
        builder.Property(x => x.EntityName)
            .HasMaxLength(100);
        builder.Property(x => x.PreviousHash)
            .HasMaxLength(200);
        builder.Property(x => x.RecordHash)
            .IsRequired()
            .HasMaxLength(200);

        builder.HasOne(x => x.AppUser)
            .WithMany()
            .HasForeignKey(x => x.AppUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class NotificationLogConfiguration : IEntityTypeConfiguration<NotificationLog>
{
    public void Configure(EntityTypeBuilder<NotificationLog> builder)
    {
        builder.ToTable("NotificationLog");

        builder.Property(x => x.NotificationType)
            .HasConversion<string>()
            .HasMaxLength(30);
        builder.Property(x => x.Message).IsRequired();

        builder.HasOne(x => x.AppUser)
            .WithMany()
            .HasForeignKey(x => x.AppUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
