using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SupplyGuard.Domain.Entities;

namespace SupplyGuard.Infrastructure.Persistence.Configurations;

public sealed class RiskScoreConfiguration : IEntityTypeConfiguration<RiskScore>
{
    public void Configure(EntityTypeBuilder<RiskScore> builder)
    {
        builder.ToTable("risk_scores");
        builder.HasKey(score => score.Id);

        builder.Property(score => score.Id).HasColumnType("uuid").ValueGeneratedNever();
        builder.Property(score => score.RiskAssessmentId).HasColumnType("uuid").IsRequired();
        builder.Property(score => score.Category).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(score => score.Score).HasPrecision(5, 2).IsRequired();
        builder.Property(score => score.Weight).HasPrecision(5, 4).IsRequired();
        builder.Property(score => score.RiskLevel).HasConversion<string>().HasMaxLength(8).IsRequired();
        builder.Property(score => score.Explanation).HasMaxLength(2000);
        builder.Property(score => score.CalculatedAtUtc).HasColumnType("timestamp with time zone").IsRequired();

        builder.HasIndex(score => new { score.RiskAssessmentId, score.Category }).IsUnique();
        builder.HasOne(score => score.RiskAssessment)
            .WithMany(assessment => assessment.RiskScores)
            .HasForeignKey(score => score.RiskAssessmentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasQueryFilter(score => !score.RiskAssessment.IsDeleted);
    }
}
