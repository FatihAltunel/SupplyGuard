using SupplyGuard.Application.Common.CQRS;
using SupplyGuard.Application.Common.Interfaces;

namespace SupplyGuard.Application.Features.RiskManagement.GetActiveEarlyWarnings;

public sealed class GetActiveEarlyWarningsQueryHandler(IRiskManagementRepository riskManagementRepository)
    : IQueryHandler<GetActiveEarlyWarningsQuery, IReadOnlyList<ActiveEarlyWarningDto>>
{
    public async Task<IReadOnlyList<ActiveEarlyWarningDto>> HandleAsync(
        GetActiveEarlyWarningsQuery query,
        CancellationToken cancellationToken = default)
    {
        var warnings = await riskManagementRepository.GetActiveEarlyWarningsAsync(
            query.SupplierId,
            cancellationToken);

        return warnings.Select(warning => new ActiveEarlyWarningDto(
                warning.Id,
                warning.SupplierId,
                warning.Supplier.Name,
                warning.RiskAssessmentId,
                warning.Title,
                warning.Message,
                warning.Severity,
                warning.Status,
                warning.DetectedAtUtc,
                warning.AcknowledgedAtUtc,
                warning.AcknowledgedByUserId))
            .ToArray();
    }
}
