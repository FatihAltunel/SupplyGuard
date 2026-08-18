using SupplyGuard.Application.Common.CQRS;

namespace SupplyGuard.Application.Features.Suppliers.GetSupplierById;

public sealed record GetSupplierByIdQuery(Guid SupplierId) : IQuery<SupplierDetailsDto?>;
