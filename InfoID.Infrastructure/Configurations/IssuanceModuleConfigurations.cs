using InfoID.Domain.Entities.IssuanceModule;
using InfoID.Domain.Entities.CardholderModule;
using InfoID.Domain.Entities.OrganizationModule;
using InfoID.Domain.Entities.TemplateModule;
using InfoID.Domain.Common.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InfoID.Infrastructure.Configurations;

public class CardConfiguration : IEntityTypeConfiguration<Card>
{
    public void Configure(EntityTypeBuilder<Card> builder)
    {
        builder.ToTable("Card");

        builder.Property(x => x.CardNumber)
            .IsRequired()
            .HasMaxLength(100);
        builder.Property(x => x.RFIDUid)
            .HasMaxLength(100);
        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.HasOne(x => x.Cardholder)
            .WithMany()
            .HasForeignKey(x => x.CardholderId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Template)
            .WithMany()
            .HasForeignKey(x => x.TemplateId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Branch)
            .WithMany()
            .HasForeignKey(x => x.BranchId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ReplacesCard)
            .WithMany()
            .HasForeignKey(x => x.ReplacesCardId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class CardStatusHistoryConfiguration : IEntityTypeConfiguration<CardStatusHistory>
{
    public void Configure(EntityTypeBuilder<CardStatusHistory> builder)
    {
        builder.ToTable("CardStatusHistory");

        builder.Property(x => x.FromStatus)
            .HasConversion<string>()
            .HasMaxLength(30);
        builder.Property(x => x.ToStatus)
            .HasConversion<string>()
            .HasMaxLength(30);
        builder.Property(x => x.Reason)
            .HasMaxLength(300);

        builder.HasOne(x => x.Card)
            .WithMany()
            .HasForeignKey(x => x.CardId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.ChangedByUser)
            .WithMany()
            .HasForeignKey(x => x.ChangedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
