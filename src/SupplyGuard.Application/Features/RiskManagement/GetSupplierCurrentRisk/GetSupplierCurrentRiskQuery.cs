using SupplyGuard.Application.Common.CQRS;
using SupplyGuard.Domain.Enums;

namespace SupplyGuard.Application.Features.RiskManagement.GetSupplierCurrentRisk;

public sealed record GetSupplierCurrentRiskQuery(Guid SupplierId)
    : IQuery<SupplierCurrentRiskDto?>;

public sealed record SupplierCurrentRiskDto(
    Guid SupplierId,
    Guid RiskAssessmentId,
    decimal OverallRiskScore,
    RiskLevel OverallRiskLevel,
    DateTimeOffset AssessedAtUtc,
    string? Rationale,
    string? Outcome,
    IReadOnlyList<RiskScoreDto> Scores);

public sealed record RiskScoreDto(
    RiskCategory Category,
    decimal Score,
    decimal Weight,
    RiskLevel RiskLevel,
    string? Explanation);
