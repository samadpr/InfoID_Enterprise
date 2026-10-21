using InfoID.LicenseManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InfoID.LicenseManager.Infrastructure.Persistence.Configurations;

internal sealed class CustomerRecordConfiguration : IEntityTypeConfiguration<CustomerRecord>
{
    public void Configure(EntityTypeBuilder<CustomerRecord> e)
    {
        e.ToTable("CustomerRecord");
        e.Property(x => x.OrganizationName).HasMaxLength(200).IsRequired();
        e.Property(x => x.OrganizationType).HasMaxLength(50);
        e.Property(x => x.Country).HasMaxLength(100);
        e.Property(x => x.ContactEmail).HasMaxLength(200);
        e.Property(x => x.ContactPhone).HasMaxLength(50);
        e.HasIndex(x => x.ContactEmail);
    }
}

internal sealed class CustomerAccountConfiguration : IEntityTypeConfiguration<CustomerAccount>
{
    public void Configure(EntityTypeBuilder<CustomerAccount> e)
    {
        e.ToTable("CustomerAccount");
        e.Property(x => x.FullName).HasMaxLength(200).IsRequired();
        e.Property(x => x.CompanyName).HasMaxLength(200);
        e.Property(x => x.Email).HasMaxLength(200).IsRequired();
        e.HasIndex(x => x.Email).IsUnique();
        e.Property(x => x.Phone).HasMaxLength(50);
        e.Property(x => x.Country).HasMaxLength(100);
        e.Property(x => x.PasswordHash).HasMaxLength(300).IsRequired();
        e.HasOne(x => x.CustomerRecord).WithMany().HasForeignKey(x => x.CustomerRecordId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class EmailCodeConfiguration : IEntityTypeConfiguration<EmailCode>
{
    public void Configure(EntityTypeBuilder<EmailCode> e)
    {
        e.ToTable("EmailCode");
        e.Property(x => x.Purpose).HasMaxLength(30).IsRequired();
        e.Property(x => x.CodeHash).HasMaxLength(200).IsRequired();
        e.HasIndex(x => new { x.CustomerAccountId, x.Purpose });
        e.HasOne(x => x.CustomerAccount).WithMany().HasForeignKey(x => x.CustomerAccountId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class CustomerSessionConfiguration : IEntityTypeConfiguration<CustomerSession>
{
    public void Configure(EntityTypeBuilder<CustomerSession> e)
    {
        e.ToTable("CustomerSession");
        e.Property(x => x.TokenHash).HasMaxLength(100).IsRequired();
        e.HasIndex(x => x.TokenHash).IsUnique();
        e.Property(x => x.CreatedFromIp).HasMaxLength(50);
        e.Property(x => x.AppVersion).HasMaxLength(50);
        e.HasOne(x => x.CustomerAccount).WithMany().HasForeignKey(x => x.CustomerAccountId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SupportTicketConfiguration : IEntityTypeConfiguration<SupportTicket>
{
    public void Configure(EntityTypeBuilder<SupportTicket> e)
    {
        e.ToTable("SupportTicket");
        e.Property(x => x.Subject).HasMaxLength(250).IsRequired();
        e.Property(x => x.Status).HasMaxLength(30).IsRequired();
        e.HasOne(x => x.CustomerRecord).WithMany().HasForeignKey(x => x.CustomerRecordId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.LicenseCode).WithMany().HasForeignKey(x => x.LicenseCodeId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.AssignedAdminUser).WithMany().HasForeignKey(x => x.AssignedAdminUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class EmailTemplateConfiguration : IEntityTypeConfiguration<EmailTemplate>
{
    public void Configure(EntityTypeBuilder<EmailTemplate> e)
    {
        e.ToTable("EmailTemplate");
        e.Property(x => x.Name).HasMaxLength(150).IsRequired();
        e.Property(x => x.Subject).HasMaxLength(300).IsRequired();
        e.Property(x => x.TemplateType).HasMaxLength(50).IsRequired();
    }
}
