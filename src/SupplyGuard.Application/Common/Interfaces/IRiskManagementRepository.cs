using SupplyGuard.Domain.Entities;

namespace SupplyGuard.Application.Common.Interfaces;

public interface IRiskManagementRepository
{
    Task<Supplier?> GetSupplierForEvaluationAsync(
        Guid supplierId,
        CancellationToken cancellationToken = default);

    Task<Supplier?> GetSupplierWithWarningsAsync(
        Guid supplierId,
        CancellationToken cancellationToken = default);

    Task<RiskAssessment?> GetCurrentRiskAssessmentAsync(
        Guid supplierId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EarlyWarning>> GetActiveEarlyWarningsAsync(
        Guid? supplierId,
        CancellationToken cancellationToken = default);

    Task AddXAIAuditLogAsync(
        XAIAuditLog xaiAuditLog,
        CancellationToken cancellationToken = default);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
