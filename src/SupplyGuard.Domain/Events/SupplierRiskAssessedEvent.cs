using SupplyGuard.Domain.Common;
using SupplyGuard.Domain.Enums;

namespace SupplyGuard.Domain.Events;

public sealed record SupplierRiskAssessedEvent(
    Guid SupplierId,
    Guid RiskAssessmentId,
    decimal OverallRiskScore,
    RiskLevel OverallRiskLevel,
    string CorrelationId,
    DateTimeOffset OccurredAtUtc) : IDomainEvent;
