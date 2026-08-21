using SupplyGuard.Application.Common.Caching;
using SupplyGuard.Application.Common.CQRS;

namespace SupplyGuard.Application.Features.Suppliers.GetSupplierById;

public sealed record GetSupplierByIdQuery(Guid SupplierId)
    : IQuery<SupplierDetailsDto?>, ICacheableQuery
{
    public string CacheKey => CacheKeys.SupplierDetails(SupplierId);
    public TimeSpan CacheDuration => TimeSpan.FromMinutes(10);
}
