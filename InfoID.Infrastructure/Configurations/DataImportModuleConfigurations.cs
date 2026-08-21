using InfoID.Domain.Entities.DataImportModule;
using InfoID.Domain.Entities.OrganizationModule;
using InfoID.Domain.Entities.TemplateModule;
using InfoID.Domain.Common.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InfoID.Infrastructure.Configurations;

public class DataSourceConnectionConfiguration : IEntityTypeConfiguration<DataSourceConnection>
{
    public void Configure(EntityTypeBuilder<DataSourceConnection> builder)
    {
        builder.ToTable("DataSourceConnection");

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(200);
        builder.Property(x => x.SourceType)
            .HasConversion<string>()
            .HasMaxLength(30);
        builder.Property(x => x.ScheduleCron)
            .HasMaxLength(100);

        builder.HasOne(x => x.Organization)
            .WithMany()
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class FieldMappingProfileConfiguration : IEntityTypeConfiguration<FieldMappingProfile>
{
    public void Configure(EntityTypeBuilder<FieldMappingProfile> builder)
    {
        builder.ToTable("FieldMappingProfile");

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(200);
        builder.Property(x => x.MappingJson).IsRequired();

        builder.HasOne(x => x.DataSourceConnection)
            .WithMany()
            .HasForeignKey(x => x.DataSourceConnectionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Template)
            .WithMany()
            .HasForeignKey(x => x.TemplateId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class ImportBatchConfiguration : IEntityTypeConfiguration<ImportBatch>
{
    public void Configure(EntityTypeBuilder<ImportBatch> builder)
    {
        builder.ToTable("ImportBatch");

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.HasOne(x => x.DataSourceConnection)
            .WithMany()
            .HasForeignKey(x => x.DataSourceConnectionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.FieldMappingProfile)
            .WithMany()
            .HasForeignKey(x => x.FieldMappingProfileId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Branch)
            .WithMany()
            .HasForeignKey(x => x.BranchId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class ImportErrorLogConfiguration : IEntityTypeConfiguration<ImportErrorLog>
{
    public void Configure(EntityTypeBuilder<ImportErrorLog> builder)
    {
        builder.ToTable("ImportErrorLog");

        builder.Property(x => x.FieldName)
            .HasMaxLength(150);
        builder.Property(x => x.ErrorMessage).IsRequired();

        builder.HasOne(x => x.ImportBatch)
            .WithMany()
            .HasForeignKey(x => x.ImportBatchId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
