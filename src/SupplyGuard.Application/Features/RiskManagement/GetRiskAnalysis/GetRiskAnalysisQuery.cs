using SupplyGuard.Application.Common.CQRS;

namespace SupplyGuard.Application.Features.RiskManagement.GetRiskAnalysis;

/// <summary>Read-only status of one JIT analysis, used by clients that poll after a 202 response.</summary>
public sealed record GetRiskAnalysisQuery(Guid SupplierId, string CorrelationId) : IQuery<RiskAnalysisStatusDto?>;

public sealed record RiskAnalysisStatusDto(
    Guid SupplierId,
    string CorrelationId,
    string Status,
    decimal? ConfidenceScore,
    string? ExplanationText,
    string? FailureMessage,
    DateTimeOffset? RequestedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    RiskAnalysisFeaturesDto? Features);

/// <summary>The six model inputs exactly as they were sent to the worker.</summary>
public sealed record RiskAnalysisFeaturesDto(
    decimal AverageDeliveryDelayDays,
    decimal OnTimeDeliveryRate,
    decimal QualityDefectRate,
    decimal DeliveryDelayTrendPercentage,
    decimal FinancialStabilityScore,
    int ActiveRiskIndicatorsCount);
