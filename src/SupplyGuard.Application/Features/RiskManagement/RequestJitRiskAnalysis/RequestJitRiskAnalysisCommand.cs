using SupplyGuard.Application.Common.CQRS;

namespace SupplyGuard.Application.Features.RiskManagement.RequestJitRiskAnalysis;

// The three base metrics come from an upstream, aggregated supplier-performance snapshot.
// They cannot be inferred from risk_indicators without purchase-order/quality observations.
public sealed record RequestJitRiskAnalysisCommand(
    Guid SupplierId,
    string CorrelationId,
    decimal AverageDeliveryDelayDays,
    decimal OnTimeDeliveryRate,
    decimal QualityDefectRate) : ICommand<RequestJitRiskAnalysisResult>;

public sealed record RequestJitRiskAnalysisResult(Guid SupplierId, string CorrelationId, Guid RequestMessageId);
