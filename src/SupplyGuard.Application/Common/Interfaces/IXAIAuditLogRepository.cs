using SupplyGuard.Domain.Entities;

namespace SupplyGuard.Application.Common.Interfaces;

public interface IXAIAuditLogRepository
{
    Task<XAIAuditLog?> GetByCorrelationIdAsync(string correlationId, CancellationToken cancellationToken = default);
    /// <summary>Requires an active transaction; the PostgreSQL row lock lasts until it ends.</summary>
    Task<XAIAuditLog?> GetByCorrelationIdForUpdateAsync(string correlationId, CancellationToken cancellationToken = default);
    Task AddAsync(XAIAuditLog audit, CancellationToken cancellationToken = default);
}
