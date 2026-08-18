using SupplyGuard.Application.Common.CQRS;
using SupplyGuard.Domain.Enums;

namespace SupplyGuard.Application.Features.Suppliers.ChangeSupplierStatus;

public sealed record ChangeSupplierStatusCommand(Guid SupplierId, SupplierStatus Status) : ICommand<Guid>;
