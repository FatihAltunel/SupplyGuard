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
    RiskAnalysisFeaturesDto? Features,
    RiskAnalysisExplanationDto? Explanation = null);

/// <summary>
/// SHAP decomposition of a completed analysis, in model log-odds: BaseValue + sum(Contribution)
/// is the risk score before the sigmoid. Null for failed analyses and for older worker responses.
/// </summary>
public sealed record RiskAnalysisExplanationDto(decimal BaseValue, IReadOnlyList<RiskAnalysisContributionDto> Contributions);

/// <summary>Feature uses the same camelCase keys as <see cref="RiskAnalysisFeaturesDto"/>.</summary>
public sealed record RiskAnalysisContributionDto(string Feature, decimal Contribution);

/// <summary>The six model inputs exactly as they were sent to the worker.</summary>
public sealed record RiskAnalysisFeaturesDto(
    decimal AverageDeliveryDelayDays,
    decimal OnTimeDeliveryRate,
    decimal QualityDefectRate,
    decimal DeliveryDelayTrendPercentage,
    decimal FinancialStabilityScore,
    int ActiveRiskIndicatorsCount);
