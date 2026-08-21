using InfoID.Domain.Entities.CardholderModule;
using InfoID.Domain.Entities.DataImportModule;
using InfoID.Domain.Entities.OrganizationModule;
using InfoID.Domain.Common.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InfoID.Infrastructure.Configurations;

public class CardholderConfiguration : IEntityTypeConfiguration<Cardholder>
{
    public void Configure(EntityTypeBuilder<Cardholder> builder)
    {
        builder.ToTable("Cardholder");

        builder.Property(x => x.FullName)
            .IsRequired()
            .HasMaxLength(200);
        builder.Property(x => x.ExternalRefId)
            .HasMaxLength(100);
        builder.Property(x => x.Email)
            .HasMaxLength(200);
        builder.Property(x => x.Phone)
            .HasMaxLength(50);
        builder.Property(x => x.Gender)
            .HasMaxLength(20);
        builder.Property(x => x.Department)
            .HasMaxLength(150);
        builder.Property(x => x.Designation)
            .HasMaxLength(150);
        builder.Property(x => x.IdNumber)
            .HasMaxLength(100);

        builder.HasOne(x => x.Organization)
            .WithMany()
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Branch)
            .WithMany()
            .HasForeignKey(x => x.BranchId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ImportBatch)
            .WithMany()
            .HasForeignKey(x => x.ImportBatchId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.CurrentPhoto)
            .WithMany()
            .HasForeignKey(x => x.CurrentPhotoId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.CurrentSignature)
            .WithMany()
            .HasForeignKey(x => x.CurrentSignatureId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class CustomFieldDefinitionConfiguration : IEntityTypeConfiguration<CustomFieldDefinition>
{
    public void Configure(EntityTypeBuilder<CustomFieldDefinition> builder)
    {
        builder.ToTable("CustomFieldDefinition");

        builder.Property(x => x.FieldKey)
            .IsRequired()
            .HasMaxLength(100);
        builder.Property(x => x.Label)
            .IsRequired()
            .HasMaxLength(200);
        builder.Property(x => x.DataType)
            .HasConversion<string>()
            .HasMaxLength(30);
        builder.Property(x => x.AppliesToCategory)
            .HasMaxLength(100);

        builder.HasOne(x => x.Organization)
            .WithMany()
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class CardholderCustomFieldValueConfiguration : IEntityTypeConfiguration<CardholderCustomFieldValue>
{
    public void Configure(EntityTypeBuilder<CardholderCustomFieldValue> builder)
    {
        builder.ToTable("CardholderCustomFieldValue");


        builder.HasOne(x => x.Cardholder)
            .WithMany()
            .HasForeignKey(x => x.CardholderId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.CustomFieldDefinition)
            .WithMany()
            .HasForeignKey(x => x.CustomFieldDefinitionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class CardholderPhotoConfiguration : IEntityTypeConfiguration<CardholderPhoto>
{
    public void Configure(EntityTypeBuilder<CardholderPhoto> builder)
    {
        builder.ToTable("CardholderPhoto");

        builder.Property(x => x.FilePath)
            .IsRequired()
            .HasMaxLength(500);
        builder.Property(x => x.CaptureSource)
            .HasConversion<string>()
            .HasMaxLength(30);
        builder.Property(x => x.ComplianceStatus)
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.HasOne(x => x.Cardholder)
            .WithMany()
            .HasForeignKey(x => x.CardholderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class CardholderSignatureConfiguration : IEntityTypeConfiguration<CardholderSignature>
{
    public void Configure(EntityTypeBuilder<CardholderSignature> builder)
    {
        builder.ToTable("CardholderSignature");

        builder.Property(x => x.FilePath)
            .IsRequired()
            .HasMaxLength(500);
        builder.Property(x => x.CaptureSource)
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.HasOne(x => x.Cardholder)
            .WithMany()
            .HasForeignKey(x => x.CardholderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
