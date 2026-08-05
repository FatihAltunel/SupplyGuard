using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SupplyGuard.Domain.Entities;

namespace SupplyGuard.Infrastructure.Persistence.Configurations;

public sealed class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> builder)
    {
        builder.ToTable("suppliers");
        builder.HasKey(supplier => supplier.Id);

        builder.Property(supplier => supplier.Id).HasColumnType("uuid").ValueGeneratedNever();
        builder.Property(supplier => supplier.Name).HasMaxLength(200).IsRequired();
        builder.Property(supplier => supplier.TaxNumber).HasMaxLength(64).IsRequired();
        builder.Property(supplier => supplier.CountryCode).HasMaxLength(2).IsRequired();
        builder.Property(supplier => supplier.RegistrationNumber).HasMaxLength(64);
        builder.Property(supplier => supplier.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(supplier => supplier.ContactName).HasMaxLength(150);
        builder.Property(supplier => supplier.ContactEmail).HasMaxLength(320);
        builder.Property(supplier => supplier.ContactPhone).HasMaxLength(32);
        builder.Property(supplier => supplier.WebsiteUrl).HasMaxLength(2048);
        builder.Property(supplier => supplier.City).HasMaxLength(100);
        builder.Property(supplier => supplier.Address).HasMaxLength(500);
        builder.Property(supplier => supplier.Industry).HasMaxLength(100);
        builder.Property(supplier => supplier.SupplierCategory).HasMaxLength(100);
        builder.Property(supplier => supplier.IsCriticalSupplier).IsRequired();
        builder.Property(supplier => supplier.OnboardingDateUtc).HasColumnType("timestamp with time zone");
        builder.Property(supplier => supplier.LastRiskAssessmentAtUtc).HasColumnType("timestamp with time zone");
        ConfigureAuditProperties(builder);

        builder.HasIndex(supplier => new { supplier.CountryCode, supplier.TaxNumber })
            .IsUnique()
            .HasDatabaseName("UX_Suppliers_CountryCode_TaxNumber");

        builder.Navigation(supplier => supplier.RiskAssessments).HasField("_riskAssessments").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(supplier => supplier.RiskIndicators).HasField("_riskIndicators").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(supplier => supplier.EarlyWarnings).HasField("_earlyWarnings").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(supplier => supplier.XAIAuditLogs).HasField("_xaiAuditLogs").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasQueryFilter(supplier => !supplier.IsDeleted);
    }

    private static void ConfigureAuditProperties(EntityTypeBuilder<Supplier> builder)
    {
        builder.Property(supplier => supplier.CreatedByUserId).HasColumnType("uuid");
        builder.Property(supplier => supplier.CreatedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(supplier => supplier.LastModifiedByUserId).HasColumnType("uuid");
        builder.Property(supplier => supplier.LastModifiedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(supplier => supplier.IsDeleted).IsRequired();
        builder.Property(supplier => supplier.DeletedAtUtc).HasColumnType("timestamp with time zone");
    }
}
