using SupplyGuard.Application.Common.CQRS;
using SupplyGuard.Domain.Enums;

namespace SupplyGuard.Application.Features.RiskManagement.EvaluateSupplierRisk;

public sealed record EvaluateSupplierRiskCommand(
    Guid SupplierId,
    string CorrelationId) : ICommand<EvaluateSupplierRiskResult>;

public sealed record EvaluateSupplierRiskResult(
    Guid SupplierId,
    Guid RiskAssessmentId,
    decimal OverallRiskScore,
    RiskLevel OverallRiskLevel,
    Guid? EarlyWarningId,
    DateTimeOffset AssessedAtUtc);
