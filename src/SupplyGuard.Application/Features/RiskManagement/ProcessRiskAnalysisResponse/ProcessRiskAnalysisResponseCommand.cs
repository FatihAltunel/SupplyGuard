using SupplyGuard.Application.Common.CQRS;
using SupplyGuard.Application.Common.Messaging.RiskAnalysis.Worker.V1;

namespace SupplyGuard.Application.Features.RiskManagement.ProcessRiskAnalysisResponse;

public sealed record ProcessRiskAnalysisResponseCommand(RiskAnalysisResponse Response)
    : ICommand<ProcessRiskAnalysisResponseResult>;

public enum RiskAnalysisResponseDisposition { Processed, Duplicate }
public sealed record ProcessRiskAnalysisResponseResult(RiskAnalysisResponseDisposition Disposition);
