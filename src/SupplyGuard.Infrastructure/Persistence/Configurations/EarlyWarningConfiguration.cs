using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SupplyGuard.Domain.Entities;

namespace SupplyGuard.Infrastructure.Persistence.Configurations;

public sealed class EarlyWarningConfiguration : IEntityTypeConfiguration<EarlyWarning>
{
    public void Configure(EntityTypeBuilder<EarlyWarning> builder)
    {
        builder.ToTable("early_warnings");
        builder.HasKey(warning => warning.Id);

        builder.Property(warning => warning.Id).HasColumnType("uuid").ValueGeneratedNever();
        builder.Property(warning => warning.SupplierId).HasColumnType("uuid").IsRequired();
        builder.Property(warning => warning.RiskAssessmentId).HasColumnType("uuid");
        builder.Property(warning => warning.Title).HasMaxLength(200).IsRequired();
        builder.Property(warning => warning.Message).HasMaxLength(4000).IsRequired();
        builder.Property(warning => warning.Severity).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(warning => warning.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(warning => warning.DetectedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(warning => warning.AcknowledgedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(warning => warning.AcknowledgedByUserId).HasColumnType("uuid");
        builder.Property(warning => warning.ResolvedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(warning => warning.ResolvedByUserId).HasColumnType("uuid");
        builder.Property(warning => warning.ResolutionNote).HasMaxLength(2000);
        builder.Property(warning => warning.CreatedByUserId).HasColumnType("uuid");
        builder.Property(warning => warning.CreatedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(warning => warning.LastModifiedByUserId).HasColumnType("uuid");
        builder.Property(warning => warning.LastModifiedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(warning => warning.IsDeleted).IsRequired();
        builder.Property(warning => warning.DeletedAtUtc).HasColumnType("timestamp with time zone");

        builder.HasIndex(warning => new { warning.SupplierId, warning.Status, warning.DetectedAtUtc });
        builder.HasOne(warning => warning.Supplier)
            .WithMany(supplier => supplier.EarlyWarnings)
            .HasForeignKey(warning => warning.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(warning => warning.RiskAssessment)
            .WithMany()
            .HasForeignKey(warning => warning.RiskAssessmentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasQueryFilter(warning => !warning.IsDeleted);
    }
}
