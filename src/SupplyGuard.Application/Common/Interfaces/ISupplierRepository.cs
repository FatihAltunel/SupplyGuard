using SupplyGuard.Domain.Entities;

namespace SupplyGuard.Application.Common.Interfaces;

public interface ISupplierRepository
{
    Task<bool> ExistsByBusinessKeyAsync(
        string countryCode,
        string taxNumber,
        CancellationToken cancellationToken = default);

    Task AddAsync(Supplier supplier, CancellationToken cancellationToken = default);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
