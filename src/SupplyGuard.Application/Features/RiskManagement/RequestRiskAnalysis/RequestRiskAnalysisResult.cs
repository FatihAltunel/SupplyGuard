namespace SupplyGuard.Application.Features.RiskManagement.RequestRiskAnalysis;

public sealed record RequestRiskAnalysisResult(Guid SupplierId, Guid CorrelationId, Guid RequestMessageId);
