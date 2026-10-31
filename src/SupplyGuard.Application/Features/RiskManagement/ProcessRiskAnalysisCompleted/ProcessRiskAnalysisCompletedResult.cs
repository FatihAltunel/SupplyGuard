namespace SupplyGuard.Application.Features.RiskManagement.ProcessRiskAnalysisCompleted;

public enum RiskAnalysisCompletionDisposition
{
    Processed,
    Duplicate
}

public sealed record ProcessRiskAnalysisCompletedResult(
    Guid SupplierId,
    Guid? RiskAssessmentId,
    RiskAnalysisCompletionDisposition Disposition);
