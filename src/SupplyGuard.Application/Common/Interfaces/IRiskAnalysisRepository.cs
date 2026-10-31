using SupplyGuard.Domain.Entities;

namespace SupplyGuard.Application.Common.Interfaces;

public interface IRiskAnalysisRepository
{
    /// <summary>Checks globally, including audits hidden by supplier soft-delete query filters.</summary>
    Task<bool> AuditExistsByCorrelationIdAsync(string correlationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a tracked, non-deleted supplier under a row lock held until the current
    /// unit of work ends. The caller must begin a transaction first. Do not load all history.
    /// </summary>
    Task<Supplier?> GetSupplierForAnalysisAsync(Guid supplierId, CancellationToken cancellationToken = default);

    /// <summary>Stages a new audit without saving, updating, or deleting existing audit records.</summary>
    Task AddXAIAuditLogAsync(XAIAuditLog auditLog, CancellationToken cancellationToken = default);
}
