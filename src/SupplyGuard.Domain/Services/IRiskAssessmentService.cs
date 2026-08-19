using SupplyGuard.Domain.Entities;

namespace SupplyGuard.Domain.Services;

public interface IRiskAssessmentService
{
    RiskAssessmentResult Evaluate(
        Supplier supplier,
        IReadOnlyCollection<RiskIndicator> indicators,
        DateTimeOffset assessedAtUtc,
        Guid? createdByUserId = null);
}
