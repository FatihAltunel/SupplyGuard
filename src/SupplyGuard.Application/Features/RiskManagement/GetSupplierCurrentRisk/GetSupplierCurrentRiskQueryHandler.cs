using SupplyGuard.Application.Common.CQRS;
using SupplyGuard.Application.Common.Interfaces;

namespace SupplyGuard.Application.Features.RiskManagement.GetSupplierCurrentRisk;

public sealed class GetSupplierCurrentRiskQueryHandler(IRiskManagementRepository riskManagementRepository)
    : IQueryHandler<GetSupplierCurrentRiskQuery, SupplierCurrentRiskDto?>
{
    public async Task<SupplierCurrentRiskDto?> HandleAsync(
        GetSupplierCurrentRiskQuery query,
        CancellationToken cancellationToken = default)
    {
        var assessment = await riskManagementRepository.GetCurrentRiskAssessmentAsync(
            query.SupplierId,
            cancellationToken);

        return assessment is null
            ? null
            : new SupplierCurrentRiskDto(
                assessment.SupplierId,
                assessment.Id,
                assessment.OverallRiskScore,
                assessment.OverallRiskLevel,
                assessment.AssessedAtUtc,
                assessment.Rationale,
                assessment.Outcome,
                assessment.RiskScores
                    .OrderBy(score => score.Category)
                    .Select(score => new RiskScoreDto(
                        score.Category,
                        score.Score,
                        score.Weight,
                        score.RiskLevel,
                        score.Explanation))
                    .ToArray());
    }
}
