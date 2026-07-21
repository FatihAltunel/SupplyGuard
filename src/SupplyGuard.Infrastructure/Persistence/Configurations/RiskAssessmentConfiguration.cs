using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SupplyGuard.Domain.Entities;

namespace SupplyGuard.Infrastructure.Persistence.Configurations;

public sealed class RiskAssessmentConfiguration : IEntityTypeConfiguration<RiskAssessment>
{
    public void Configure(EntityTypeBuilder<RiskAssessment> builder)
    {
        builder.ToTable("risk_assessments");
        builder.HasKey(assessment => assessment.Id);

        builder.Property(assessment => assessment.Id).HasColumnType("uuid").ValueGeneratedNever();
        builder.Property(assessment => assessment.SupplierId).HasColumnType("uuid").IsRequired();
        builder.Property(assessment => assessment.OverallRiskScore).HasPrecision(5, 2).IsRequired();
        builder.Property(assessment => assessment.OverallRiskLevel).HasConversion<string>().HasMaxLength(8).IsRequired();
        builder.Property(assessment => assessment.AssessedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(assessment => assessment.Rationale).HasMaxLength(4000);
        builder.Property(assessment => assessment.Outcome).HasMaxLength(1000);
        builder.Property(assessment => assessment.CreatedByUserId).HasColumnType("uuid");
        builder.Property(assessment => assessment.CreatedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(assessment => assessment.LastModifiedByUserId).HasColumnType("uuid");
        builder.Property(assessment => assessment.LastModifiedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(assessment => assessment.IsDeleted).IsRequired();
        builder.Property(assessment => assessment.DeletedAtUtc).HasColumnType("timestamp with time zone");

        builder.HasIndex(assessment => new { assessment.SupplierId, assessment.AssessedAtUtc });
        builder.HasOne(assessment => assessment.Supplier)
            .WithMany(supplier => supplier.RiskAssessments)
            .HasForeignKey(assessment => assessment.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(assessment => assessment.RiskScores).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasQueryFilter(assessment => !assessment.IsDeleted);
    }
}
