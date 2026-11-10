using Microsoft.EntityFrameworkCore;
using SupplyGuard.Application.Common.Interfaces;

namespace SupplyGuard.Infrastructure.Persistence.Repositories;

public sealed class SupplierRiskIndicatorReader(SupplyGuardDbContext dbContext) : ISupplierRiskIndicatorReader
{
    public Task<int> CountActiveAsync(Guid supplierId, CancellationToken cancellationToken = default) =>
        dbContext.RiskIndicators.AsNoTracking()
            .CountAsync(indicator => indicator.SupplierId == supplierId && indicator.IsActive, cancellationToken);
}
