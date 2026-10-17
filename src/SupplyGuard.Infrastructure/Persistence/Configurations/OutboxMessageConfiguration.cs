using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SupplyGuard.Application.Common.Messaging;

namespace SupplyGuard.Infrastructure.Persistence.Configurations;

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages", SupplyGuardDbContext.DomainSchema);
        builder.HasKey(message => message.Id);

        builder.Property(message => message.Id).HasColumnType("uuid").ValueGeneratedNever();
        builder.Property(message => message.MessageType)
            .HasMaxLength(OutboxMessage.MessageTypeMaxLength).IsRequired();
        builder.Property(message => message.Payload).HasColumnType("jsonb").IsRequired();
        builder.Property(message => message.CorrelationId)
            .HasMaxLength(OutboxMessage.CorrelationIdMaxLength).IsRequired();
        builder.Property(message => message.OccurredAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(message => message.NextAttemptAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(message => message.ProcessedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(message => message.RetryCount).IsRequired();
        builder.Property(message => message.LastError).HasMaxLength(OutboxMessage.LastErrorMaxLength);

        builder.HasIndex(message => new { message.NextAttemptAtUtc, message.OccurredAtUtc, message.Id })
            .HasDatabaseName("ix_outbox_messages_pending")
            .HasFilter("\"ProcessedAtUtc\" IS NULL");
    }
}
