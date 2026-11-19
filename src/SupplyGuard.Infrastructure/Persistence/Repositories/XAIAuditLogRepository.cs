using Microsoft.EntityFrameworkCore;
using SupplyGuard.Application.Common.Interfaces;
using SupplyGuard.Domain.Entities;

namespace SupplyGuard.Infrastructure.Persistence.Repositories;

public sealed class XAIAuditLogRepository(SupplyGuardDbContext context) : IXAIAuditLogRepository
{
    public Task<XAIAuditLog?> GetByCorrelationIdAsync(string correlationId, CancellationToken cancellationToken = default) =>
        context.XAIAuditLogs.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(audit => audit.CorrelationId == correlationId, cancellationToken);

    public async Task<XAIAuditLog?> GetByCorrelationIdForUpdateAsync(string correlationId, CancellationToken cancellationToken = default)
    {
        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Audit row locking requires an active transaction.");
        if (context.Database.IsNpgsql())
        {
            var rows = await context.XAIAuditLogs.FromSqlInterpolated($"""
                SELECT * FROM supplyguard.xai_audit_logs WHERE "CorrelationId" = {correlationId} FOR UPDATE
                """).IgnoreQueryFilters().AsTracking().ToListAsync(cancellationToken);
            return rows.SingleOrDefault();
        }
        if (context.Database.ProviderName != "Microsoft.EntityFrameworkCore.Sqlite")
            throw new NotSupportedException("Audit locking requires PostgreSQL (SQLite is supported only for workflow tests).");
        return await context.XAIAuditLogs.IgnoreQueryFilters()
            .SingleOrDefaultAsync(audit => audit.CorrelationId == correlationId, cancellationToken);
    }

    public Task AddAsync(XAIAuditLog audit, CancellationToken cancellationToken = default) =>
        context.XAIAuditLogs.AddAsync(audit, cancellationToken).AsTask();
}
