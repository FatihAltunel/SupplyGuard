using SupplyGuard.Application.Common.CQRS;

namespace SupplyGuard.Application.Features.Suppliers.GetSuppliers;

public sealed record GetSuppliersQuery(int PageNumber = 1, int PageSize = 20)
    : IQuery<PagedResult<SupplierListItemDto>>;
