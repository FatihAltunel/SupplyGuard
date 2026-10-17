using System.Text.Json;
using SupplyGuard.Application.Common.Interfaces;
using SupplyGuard.Application.Common.Messaging;

namespace SupplyGuard.Infrastructure.Persistence.Outbox;

public sealed class OutboxWriter(SupplyGuardDbContext dbContext) : IOutboxWriter
{
    public Task WriteAsync(
        IIntegrationMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();

        // Use the concrete type so warning-specific fields are retained in the payload.
        var payload = JsonSerializer.Serialize(message, message.GetType());
        var outboxMessage = new OutboxMessage(
            message.MessageId,
            message.MessageType,
            payload,
            message.CorrelationId,
            message.OccurredAtUtc);

        // This context is shared with the repositories; only the command handler saves it.
        return dbContext.OutboxMessages.AddAsync(outboxMessage, cancellationToken).AsTask();
    }
}
