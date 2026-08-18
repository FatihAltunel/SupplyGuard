using Microsoft.EntityFrameworkCore;
using SupplyGuard.Application.Common.Interfaces;
using SupplyGuard.Domain.Entities;

namespace SupplyGuard.Infrastructure.Persistence.Repositories;

public sealed class SupplierRepository(SupplyGuardDbContext dbContext) : ISupplierRepository
{
    public Task<bool> ExistsByBusinessKeyAsync(
        string countryCode,
        string taxNumber,
        CancellationToken cancellationToken = default) =>
        dbContext.Suppliers.AnyAsync(
            supplier => supplier.CountryCode == countryCode && supplier.TaxNumber == taxNumber,
            cancellationToken);

    public Task AddAsync(Supplier supplier, CancellationToken cancellationToken = default) =>
        dbContext.Suppliers.AddAsync(supplier, cancellationToken).AsTask();

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
