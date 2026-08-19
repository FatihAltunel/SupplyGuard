using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SupplyGuard.Domain.Entities;

namespace SupplyGuard.Infrastructure.Persistence.Configurations;

public sealed class XAIAuditLogConfiguration : IEntityTypeConfiguration<XAIAuditLog>
{
    public void Configure(EntityTypeBuilder<XAIAuditLog> builder)
    {
        builder.ToTable("xai_audit_logs");
        builder.HasKey(log => log.Id);

        builder.Property(log => log.Id).HasColumnType("uuid").ValueGeneratedNever();
        builder.Property(log => log.SupplierId).HasColumnType("uuid").IsRequired();
        builder.Property(log => log.RiskAssessmentId).HasColumnType("uuid");
        builder.Property(log => log.ModelName).HasMaxLength(200).IsRequired();
        builder.Property(log => log.ModelVersion).HasMaxLength(100).IsRequired();
        builder.Property(log => log.CorrelationId).HasMaxLength(128).IsRequired();
        builder.Property(log => log.ExplanationStatus).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(log => log.RequestPayload).HasMaxLength(100000).IsRequired();
        builder.Property(log => log.ResponsePayload).HasMaxLength(100000);
        builder.Property(log => log.ConfidenceScore).HasPrecision(5, 4).IsRequired();
        builder.Property(log => log.LatencyMs).IsRequired();
        builder.Property(log => log.IsSuccessful).IsRequired();
        builder.Property(log => log.FailureCode).HasMaxLength(100);
        builder.Property(log => log.FailureMessage).HasMaxLength(2000);
        builder.Property(log => log.ExecutedAtUtc).HasColumnType("timestamp with time zone").IsRequired();

        builder.HasIndex(log => log.CorrelationId);
        builder.HasIndex(log => new { log.SupplierId, log.ExecutedAtUtc });
        builder.HasOne(log => log.Supplier)
            .WithMany(supplier => supplier.XAIAuditLogs)
            .HasForeignKey(log => log.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(log => log.RiskAssessment)
            .WithMany()
            .HasForeignKey(log => log.RiskAssessmentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasQueryFilter(log => !log.Supplier.IsDeleted);
    }
}
