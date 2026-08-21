using InfoID.Domain.Entities.SystemModule;
using InfoID.Domain.Entities.OrganizationModule;
using InfoID.Domain.Common.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InfoID.Infrastructure.Configurations;

public class ReportDefinitionConfiguration : IEntityTypeConfiguration<ReportDefinition>
{
    public void Configure(EntityTypeBuilder<ReportDefinition> builder)
    {
        builder.ToTable("ReportDefinition");

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(200);
        builder.Property(x => x.ReportType)
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.HasOne(x => x.Organization)
            .WithMany()
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.CreatedByUser)
            .WithMany()
            .HasForeignKey(x => x.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class PluginRegistrationConfiguration : IEntityTypeConfiguration<PluginRegistration>
{
    public void Configure(EntityTypeBuilder<PluginRegistration> builder)
    {
        builder.ToTable("PluginRegistration");

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(150);
        builder.Property(x => x.PluginType)
            .HasConversion<string>()
            .HasMaxLength(30);
        builder.Property(x => x.Version)
            .HasMaxLength(30);
        builder.Property(x => x.AssemblyPath)
            .HasMaxLength(500);

    }
}

public class SystemSettingConfiguration : IEntityTypeConfiguration<SystemSetting>
{
    public void Configure(EntityTypeBuilder<SystemSetting> builder)
    {
        builder.ToTable("SystemSetting");

        builder.Property(x => x.SettingKey)
            .IsRequired()
            .HasMaxLength(150);

        builder.HasOne(x => x.Organization)
            .WithMany()
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class BackupRestoreLogConfiguration : IEntityTypeConfiguration<BackupRestoreLog>
{
    public void Configure(EntityTypeBuilder<BackupRestoreLog> builder)
    {
        builder.ToTable("BackupRestoreLog");

        builder.Property(x => x.OperationType)
            .HasConversion<string>()
            .HasMaxLength(30);
        builder.Property(x => x.FilePath)
            .HasMaxLength(500);
        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(30);

    }
}
