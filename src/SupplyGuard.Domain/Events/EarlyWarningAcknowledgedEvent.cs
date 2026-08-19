using SupplyGuard.Domain.Common;

namespace SupplyGuard.Domain.Events;

public sealed record EarlyWarningAcknowledgedEvent(
    Guid SupplierId,
    Guid EarlyWarningId,
    Guid AcknowledgedByUserId,
    DateTimeOffset OccurredAtUtc) : IDomainEvent;
