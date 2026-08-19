using SupplyGuard.Domain.Common;
using SupplyGuard.Domain.Enums;

namespace SupplyGuard.Domain.Events;

public sealed record EarlyWarningTriggeredEvent(
    Guid SupplierId,
    Guid EarlyWarningId,
    Guid RiskAssessmentId,
    WarningSeverity Severity,
    string CorrelationId,
    DateTimeOffset OccurredAtUtc) : IDomainEvent;
