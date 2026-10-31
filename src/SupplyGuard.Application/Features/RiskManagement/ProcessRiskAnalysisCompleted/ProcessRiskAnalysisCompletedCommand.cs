using SupplyGuard.Application.Common.CQRS;
using SupplyGuard.Application.Common.Messaging.RiskAnalysis.V1;

namespace SupplyGuard.Application.Features.RiskManagement.ProcessRiskAnalysisCompleted;

public sealed record ProcessRiskAnalysisCompletedCommand(RiskAnalysisCompletedEvent Completion)
    : ICommand<ProcessRiskAnalysisCompletedResult>;
