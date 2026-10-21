using InfoID.LicenseManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InfoID.LicenseManager.Infrastructure.Persistence.Configurations;

internal sealed class AdminUserConfiguration : IEntityTypeConfiguration<AdminUser>
{
    public void Configure(EntityTypeBuilder<AdminUser> e)
    {
        e.ToTable("AdminUser");
        e.Property(x => x.FullName).HasMaxLength(200).IsRequired();
        e.Property(x => x.Email).HasMaxLength(200).IsRequired();
        e.HasIndex(x => x.Email).IsUnique();
        e.Property(x => x.PasswordHash).HasMaxLength(300).IsRequired();
        e.Property(x => x.Role).HasMaxLength(30).IsRequired();
    }
}

internal sealed class AdminAuditLogConfiguration : IEntityTypeConfiguration<AdminAuditLog>
{
    public void Configure(EntityTypeBuilder<AdminAuditLog> e)
    {
        e.ToTable("AdminAuditLog");
        e.Property(x => x.Action).HasMaxLength(150).IsRequired();
        e.Property(x => x.IPAddress).HasMaxLength(50);
        e.Property(x => x.PreviousHash).HasMaxLength(200);
        e.Property(x => x.RecordHash).HasMaxLength(200).IsRequired();
        e.HasIndex(x => x.ActionDate);
        e.HasOne(x => x.AdminUser).WithMany().HasForeignKey(x => x.AdminUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
