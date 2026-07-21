using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SupplyGuard.Domain.Entities;

namespace SupplyGuard.Infrastructure.Persistence.Configurations;

public sealed class RiskIndicatorConfiguration : IEntityTypeConfiguration<RiskIndicator>
{
    public void Configure(EntityTypeBuilder<RiskIndicator> builder)
    {
        builder.ToTable("risk_indicators");
        builder.HasKey(indicator => indicator.Id);

        builder.Property(indicator => indicator.Id).HasColumnType("uuid").ValueGeneratedNever();
        builder.Property(indicator => indicator.SupplierId).HasColumnType("uuid").IsRequired();
        builder.Property(indicator => indicator.Category).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(indicator => indicator.IndicatorCode).HasMaxLength(100).IsRequired();
        builder.Property(indicator => indicator.Name).HasMaxLength(200).IsRequired();
        builder.Property(indicator => indicator.Description).HasMaxLength(1000);
        builder.Property(indicator => indicator.Severity).HasConversion<string>().HasMaxLength(8).IsRequired();
        builder.Property(indicator => indicator.RawValue).HasPrecision(18, 4).IsRequired();
        builder.Property(indicator => indicator.Unit).HasMaxLength(32);
        builder.Property(indicator => indicator.NormalizedScore).HasPrecision(5, 2).IsRequired();
        builder.Property(indicator => indicator.Weight).HasPrecision(5, 4).IsRequired();
        builder.Property(indicator => indicator.SourceSystem).HasMaxLength(100).IsRequired();
        builder.Property(indicator => indicator.SourceReference).HasMaxLength(500);
        builder.Property(indicator => indicator.ObservedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(indicator => indicator.ExpiresAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(indicator => indicator.IsActive).IsRequired();
        builder.Property(indicator => indicator.CreatedByUserId).HasColumnType("uuid");
        builder.Property(indicator => indicator.CreatedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(indicator => indicator.LastModifiedByUserId).HasColumnType("uuid");
        builder.Property(indicator => indicator.LastModifiedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(indicator => indicator.IsDeleted).IsRequired();
        builder.Property(indicator => indicator.DeletedAtUtc).HasColumnType("timestamp with time zone");

        builder.HasIndex(indicator => new { indicator.SupplierId, indicator.Category, indicator.IsActive });
        builder.HasOne(indicator => indicator.Supplier)
            .WithMany(supplier => supplier.RiskIndicators)
            .HasForeignKey(indicator => indicator.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasQueryFilter(indicator => !indicator.IsDeleted);
    }
}
