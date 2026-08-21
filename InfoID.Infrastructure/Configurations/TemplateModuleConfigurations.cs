using InfoID.Domain.Entities.TemplateModule;
using InfoID.Domain.Entities.OrganizationModule;
using InfoID.Domain.Common.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InfoID.Infrastructure.Configurations;

public class TemplateConfiguration : IEntityTypeConfiguration<Template>
{
    public void Configure(EntityTypeBuilder<Template> builder)
    {
        builder.ToTable("Template");

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(200);
        builder.Property(x => x.CardSize)
            .IsRequired()
            .HasMaxLength(30);
        builder.Property(x => x.Category)
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.HasOne(x => x.Organization)
            .WithMany()
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Branch)
            .WithMany()
            .HasForeignKey(x => x.BranchId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class TemplateElementConfiguration : IEntityTypeConfiguration<TemplateElement>
{
    public void Configure(EntityTypeBuilder<TemplateElement> builder)
    {
        builder.ToTable("TemplateElement");

        builder.Property(x => x.Side)
            .HasConversion<string>()
            .HasMaxLength(30);
        builder.Property(x => x.ElementType)
            .HasConversion<string>()
            .HasMaxLength(30);
        builder.Property(x => x.BoundFieldName)
            .HasMaxLength(150);
        builder.Property(x => x.PositionX).HasPrecision(10, 2);
        builder.Property(x => x.PositionY).HasPrecision(10, 2);
        builder.Property(x => x.Width).HasPrecision(10, 2);
        builder.Property(x => x.Height).HasPrecision(10, 2);
        builder.Property(x => x.Rotation).HasPrecision(6, 2);

        builder.HasOne(x => x.Template)
            .WithMany()
            .HasForeignKey(x => x.TemplateId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class TemplateVersionConfiguration : IEntityTypeConfiguration<TemplateVersion>
{
    public void Configure(EntityTypeBuilder<TemplateVersion> builder)
    {
        builder.ToTable("TemplateVersion");

        builder.Property(x => x.SnapshotJson).IsRequired();
        builder.Property(x => x.ChangeNote)
            .HasMaxLength(500);

        builder.HasOne(x => x.Template)
            .WithMany()
            .HasForeignKey(x => x.TemplateId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
