using SupplyGuard.Domain.Common;

namespace SupplyGuard.Domain.Events;

public sealed record EarlyWarningResolvedEvent(
    Guid SupplierId,
    Guid EarlyWarningId,
    Guid ResolvedByUserId,
    string ResolutionNote,
    DateTimeOffset OccurredAtUtc) : IDomainEvent;
