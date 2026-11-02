using Microsoft.EntityFrameworkCore;
using SupplyGuard.Application.Common.Interfaces;
using SupplyGuard.Domain.Entities;
using SupplyGuard.Domain.Enums;

namespace SupplyGuard.Infrastructure.Persistence.Repositories;

public sealed class RiskManagementRepository(SupplyGuardDbContext dbContext) : IRiskManagementRepository, IRiskAnalysisRepository
{
    public Task<bool> AuditExistsByCorrelationIdAsync(string correlationId, CancellationToken cancellationToken = default) =>
        dbContext.XAIAuditLogs.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(log => log.CorrelationId == correlationId, cancellationToken);

    public async Task<Supplier?> GetSupplierForAnalysisAsync(Guid supplierId, CancellationToken cancellationToken = default)
    {
        if (dbContext.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Supplier analysis requires an active transaction.");
        if (dbContext.Database.IsNpgsql())
        {
            var suppliers = await dbContext.Suppliers.FromSqlInterpolated($"""
                SELECT * FROM supplyguard.suppliers
                WHERE "Id" = {supplierId} AND NOT "IsDeleted"
                FOR UPDATE
                """).IgnoreQueryFilters().AsTracking().ToListAsync(cancellationToken);
            return suppliers.SingleOrDefault();
        }
        if (dbContext.Database.ProviderName != "Microsoft.EntityFrameworkCore.Sqlite")
            throw new NotSupportedException("Risk analysis requires PostgreSQL (or SQLite for persistence tests).");
        return await dbContext.Suppliers.SingleOrDefaultAsync(supplier => supplier.Id == supplierId, cancellationToken);
    }

    public Task<Supplier?> GetSupplierForEvaluationAsync(
        Guid supplierId,
        CancellationToken cancellationToken = default) =>
        dbContext.Suppliers
            .AsSplitQuery()
            .Include(supplier => supplier.RiskIndicators)
            .Include(supplier => supplier.RiskAssessments)
            .Include(supplier => supplier.EarlyWarnings)
            .SingleOrDefaultAsync(supplier => supplier.Id == supplierId, cancellationToken);

    public Task<Supplier?> GetSupplierWithWarningsAsync(
        Guid supplierId,
        CancellationToken cancellationToken = default) =>
        dbContext.Suppliers
            .Include(supplier => supplier.EarlyWarnings)
            .SingleOrDefaultAsync(supplier => supplier.Id == supplierId, cancellationToken);

    public Task<RiskAssessment?> GetCurrentRiskAssessmentAsync(
        Guid supplierId,
        CancellationToken cancellationToken = default) =>
        dbContext.RiskAssessments
            .AsNoTracking()
            .Include(assessment => assessment.RiskScores)
            .Where(assessment => assessment.SupplierId == supplierId)
            .OrderByDescending(assessment => assessment.AssessedAtUtc)
            .ThenByDescending(assessment => assessment.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<EarlyWarning>> GetActiveEarlyWarningsAsync(
        Guid? supplierId,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.EarlyWarnings
            .AsNoTracking()
            .Include(warning => warning.Supplier)
            .Where(warning => warning.Status == WarningStatus.Open || warning.Status == WarningStatus.Acknowledged);

        if (supplierId is { } id)
        {
            query = query.Where(warning => warning.SupplierId == id);
        }

        return await query
            .OrderBy(warning => warning.Severity == WarningSeverity.Critical ? 0
                : warning.Severity == WarningSeverity.High ? 1
                : warning.Severity == WarningSeverity.Medium ? 2
                : warning.Severity == WarningSeverity.Low ? 3
                : 4)
            .ThenBy(warning => warning.DetectedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public Task AddXAIAuditLogAsync(
        XAIAuditLog xaiAuditLog,
        CancellationToken cancellationToken = default) =>
        dbContext.XAIAuditLogs.AddAsync(xaiAuditLog, cancellationToken).AsTask();

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
