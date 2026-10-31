using SupplyGuard.Application.Common.CQRS;
using SupplyGuard.Application.Common.Messaging.RiskAnalysis.V1;

namespace SupplyGuard.Application.Features.RiskManagement.RequestRiskAnalysis;

public sealed record RequestRiskAnalysisCommand(Guid SupplierId, RiskAnalysisInputV1 Input)
    : ICommand<RequestRiskAnalysisResult>;
