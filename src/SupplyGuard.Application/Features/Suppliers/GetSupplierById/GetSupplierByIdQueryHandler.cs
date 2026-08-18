using SupplyGuard.Application.Common.CQRS;
using SupplyGuard.Application.Common.Interfaces;

namespace SupplyGuard.Application.Features.Suppliers.GetSupplierById;

public sealed class GetSupplierByIdQueryHandler(ISupplierRepository supplierRepository)
    : IQueryHandler<GetSupplierByIdQuery, SupplierDetailsDto?>
{
    public async Task<SupplierDetailsDto?> HandleAsync(
        GetSupplierByIdQuery query,
        CancellationToken cancellationToken = default)
    {
        var supplier = await supplierRepository.GetByIdAsync(query.SupplierId, cancellationToken);
        return supplier is null
            ? null
            : new SupplierDetailsDto(
                supplier.Id,
                supplier.Name,
                supplier.TaxNumber,
                supplier.CountryCode,
                supplier.RegistrationNumber,
                supplier.Status,
                supplier.ContactName,
                supplier.ContactEmail,
                supplier.ContactPhone,
                supplier.WebsiteUrl,
                supplier.City,
                supplier.Address,
                supplier.Industry,
                supplier.SupplierCategory,
                supplier.IsCriticalSupplier,
                supplier.OnboardingDateUtc,
                supplier.LastRiskAssessmentAtUtc,
                supplier.CreatedAtUtc,
                supplier.LastModifiedAtUtc);
    }
}
