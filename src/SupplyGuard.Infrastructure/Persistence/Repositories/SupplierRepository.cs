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

    public Task<Supplier?> GetByIdAsync(Guid supplierId, CancellationToken cancellationToken = default) =>
        dbContext.Suppliers.SingleOrDefaultAsync(supplier => supplier.Id == supplierId, cancellationToken);

    public async Task<IReadOnlyList<Supplier>> GetPageAsync(
        int skip,
        int take,
        CancellationToken cancellationToken = default) =>
        await dbContext.Suppliers
            .AsNoTracking()
            .OrderBy(supplier => supplier.Name)
            .ThenBy(supplier => supplier.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

    public Task<int> GetCountAsync(CancellationToken cancellationToken = default) =>
        dbContext.Suppliers.CountAsync(cancellationToken);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
