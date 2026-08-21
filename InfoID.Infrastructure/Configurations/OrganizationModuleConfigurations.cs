using InfoID.Domain.Entities.OrganizationModule;
using InfoID.Domain.Entities.PrintingModule;
using InfoID.Domain.Entities.TemplateModule;
using InfoID.Domain.Common.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InfoID.Infrastructure.Configurations;

public class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        builder.ToTable("Organization");

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(250);
        builder.Property(x => x.OrgType)
            .HasConversion<string>()
            .HasMaxLength(30);
        builder.Property(x => x.Email)
            .HasMaxLength(200);
        builder.Property(x => x.Phone)
            .HasMaxLength(50);
        builder.Property(x => x.Country)
            .HasMaxLength(100);
        builder.Property(x => x.LogoPath)
            .HasMaxLength(500);
        builder.Property(x => x.AccentColorHex)
            .HasMaxLength(10);

    }
}

public class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> builder)
    {
        builder.ToTable("AppUser");

        builder.Property(x => x.FullName)
            .IsRequired()
            .HasMaxLength(200);
        builder.Property(x => x.Email)
            .HasMaxLength(200);
        builder.Property(x => x.Phone)
            .HasMaxLength(50);
        builder.Property(x => x.PreferredLanguage)
            .HasMaxLength(20);
        builder.Property(x => x.ThemeMode)
            .HasConversion<string>()
            .HasMaxLength(30);
        builder.Property(x => x.PasswordHash)
            .HasMaxLength(300);

        builder.HasOne(x => x.Organization)
            .WithMany()
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class BranchConfiguration : IEntityTypeConfiguration<Branch>
{
    public void Configure(EntityTypeBuilder<Branch> builder)
    {
        builder.ToTable("Branch");

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(200);
        builder.Property(x => x.Code)
            .HasMaxLength(50);
        builder.Property(x => x.NumberingPrefix)
            .HasMaxLength(20);

        builder.HasOne(x => x.Organization)
            .WithMany()
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.DefaultPrinterProfile)
            .WithMany()
            .HasForeignKey(x => x.DefaultPrinterProfileId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.DefaultTemplate)
            .WithMany()
            .HasForeignKey(x => x.DefaultTemplateId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class DepartmentConfiguration : IEntityTypeConfiguration<Department>
{
    public void Configure(EntityTypeBuilder<Department> builder)
    {
        builder.ToTable("Department");

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.HasOne(x => x.Branch)
            .WithMany()
            .HasForeignKey(x => x.BranchId)
            .OnDelete(DeleteBehavior.Restrict);

        // Self-referencing FK for the Faculty -> Department -> Class hierarchy.
        // Restrict delete so removing a parent doesn't cascade-delete a whole
        // branch of the org chart by accident.
        builder.HasOne(x => x.ParentDepartment)
            .WithMany()
            .HasForeignKey(x => x.ParentDepartmentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
