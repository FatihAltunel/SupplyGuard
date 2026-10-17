using SupplyGuard.Application.Common.Events;
using SupplyGuard.Application.Common.Interfaces;
using SupplyGuard.Application.Common.Messaging;
using SupplyGuard.Domain.Events;

namespace SupplyGuard.Application.Features.RiskManagement.Events;

public sealed class EarlyWarningTriggeredEventHandler(IOutboxWriter outboxWriter)
    : IDomainEventHandler<EarlyWarningTriggeredEvent>
{
    public Task HandleAsync(
        EarlyWarningTriggeredEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        var message = new EarlyWarningRaisedV1(
            Guid.NewGuid(),
            domainEvent.EarlyWarningId,
            domainEvent.SupplierId,
            domainEvent.RiskAssessmentId,
            domainEvent.Severity.ToString(),
            domainEvent.CorrelationId,
            domainEvent.OccurredAtUtc.ToUniversalTime());

        return outboxWriter.WriteAsync(message, cancellationToken);
    }
}
