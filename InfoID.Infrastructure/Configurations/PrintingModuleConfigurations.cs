using InfoID.Domain.Entities.PrintingModule;
using InfoID.Domain.Entities.ApprovalAuditModule;
using InfoID.Domain.Entities.IssuanceModule;
using InfoID.Domain.Entities.OrganizationModule;
using InfoID.Domain.Entities.TemplateModule;
using InfoID.Domain.Common.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InfoID.Infrastructure.Configurations;

public class PrinterProfileConfiguration : IEntityTypeConfiguration<PrinterProfile>
{
    public void Configure(EntityTypeBuilder<PrinterProfile> builder)
    {
        builder.ToTable("PrinterProfile");

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(150);
        builder.Property(x => x.Brand)
            .HasConversion<string>()
            .HasMaxLength(30);
        builder.Property(x => x.Model)
            .HasMaxLength(150);
        builder.Property(x => x.ConnectionType)
            .HasConversion<string>()
            .HasMaxLength(30);
        builder.Property(x => x.RibbonCostEstimate).HasPrecision(10, 2);
        builder.Property(x => x.CardCostEstimate).HasPrecision(10, 2);

        builder.HasOne(x => x.Organization)
            .WithMany()
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class PrintJobConfiguration : IEntityTypeConfiguration<PrintJob>
{
    public void Configure(EntityTypeBuilder<PrintJob> builder)
    {
        builder.ToTable("PrintJob");

        builder.Property(x => x.JobType)
            .HasConversion<string>()
            .HasMaxLength(30);
        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.HasOne(x => x.Branch)
            .WithMany()
            .HasForeignKey(x => x.BranchId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.PrinterProfile)
            .WithMany()
            .HasForeignKey(x => x.PrinterProfileId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Template)
            .WithMany()
            .HasForeignKey(x => x.TemplateId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ApprovalRecord)
            .WithMany()
            .HasForeignKey(x => x.ApprovalRecordId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.RequestedByUser)
            .WithMany()
            .HasForeignKey(x => x.RequestedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class PrintJobItemConfiguration : IEntityTypeConfiguration<PrintJobItem>
{
    public void Configure(EntityTypeBuilder<PrintJobItem> builder)
    {
        builder.ToTable("PrintJobItem");

        builder.Property(x => x.Outcome)
            .HasConversion<string>()
            .HasMaxLength(30);
        builder.Property(x => x.ReprintReasonCode)
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.HasOne(x => x.PrintJob)
            .WithMany()
            .HasForeignKey(x => x.PrintJobId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Card)
            .WithMany()
            .HasForeignKey(x => x.CardId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
