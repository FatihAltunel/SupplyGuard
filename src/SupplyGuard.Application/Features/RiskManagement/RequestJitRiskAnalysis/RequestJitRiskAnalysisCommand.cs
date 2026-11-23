using SupplyGuard.Application.Common.CQRS;

namespace SupplyGuard.Application.Features.RiskManagement.RequestJitRiskAnalysis;

// The three base metrics come from an upstream, aggregated supplier-performance snapshot.
// They cannot be inferred from risk_indicators without purchase-order/quality observations.
public sealed record RequestJitRiskAnalysisCommand(
    Guid SupplierId,
    string CorrelationId,
    decimal AverageDeliveryDelayDays,
    decimal OnTimeDeliveryRate,
    decimal QualityDefectRate,
    JitFeatureOverrides? Overrides = null) : ICommand<RequestJitRiskAnalysisResult>;

/// <summary>Demo-only replacements for integration-derived features; see <see cref="JitFeatureOverridePolicy"/>.</summary>
public sealed record JitFeatureOverrides(
    decimal? DeliveryDelayTrendPercentage,
    decimal? FinancialStabilityScore,
    int? ActiveRiskIndicatorsCount)
{
    public bool IsEmpty => DeliveryDelayTrendPercentage is null && FinancialStabilityScore is null && ActiveRiskIndicatorsCount is null;
}

public sealed record RequestJitRiskAnalysisResult(Guid SupplierId, string CorrelationId, Guid RequestMessageId);
