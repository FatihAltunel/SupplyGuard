using SupplyGuard.Application.Common.CQRS;
using SupplyGuard.Application.Common.Interfaces;

namespace SupplyGuard.Application.Features.Suppliers.GetSuppliers;

public sealed class GetSuppliersQueryHandler(ISupplierRepository supplierRepository)
    : IQueryHandler<GetSuppliersQuery, PagedResult<SupplierListItemDto>>
{
    private const int MaximumPageSize = 100;

    public async Task<PagedResult<SupplierListItemDto>> HandleAsync(
        GetSuppliersQuery query,
        CancellationToken cancellationToken = default)
    {
        var pageNumber = Math.Max(1, query.PageNumber);
        var pageSize = Math.Clamp(query.PageSize, 1, MaximumPageSize);
        var totalCount = await supplierRepository.GetCountAsync(cancellationToken);
        var suppliers = await supplierRepository.GetPageAsync(
            (pageNumber - 1) * pageSize,
            pageSize,
            cancellationToken);

        var items = suppliers
            .Select(supplier => new SupplierListItemDto(
                supplier.Id,
                supplier.Name,
                supplier.CountryCode,
                supplier.TaxNumber,
                supplier.Status,
                supplier.IsCriticalSupplier,
                supplier.City,
                supplier.Industry,
                supplier.LastRiskAssessmentAtUtc))
            .ToArray();

        return new PagedResult<SupplierListItemDto>(items, pageNumber, pageSize, totalCount);
    }
}
