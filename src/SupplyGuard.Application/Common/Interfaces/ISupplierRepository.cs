using SupplyGuard.Domain.Entities;

namespace SupplyGuard.Application.Common.Interfaces;

public interface ISupplierRepository
{
    Task<bool> ExistsByBusinessKeyAsync(
        string countryCode,
        string taxNumber,
        CancellationToken cancellationToken = default);

    Task AddAsync(Supplier supplier, CancellationToken cancellationToken = default);

    Task<Supplier?> GetByIdAsync(Guid supplierId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Supplier>> GetPageAsync(
        int skip,
        int take,
        CancellationToken cancellationToken = default);

    Task<int> GetCountAsync(CancellationToken cancellationToken = default);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
