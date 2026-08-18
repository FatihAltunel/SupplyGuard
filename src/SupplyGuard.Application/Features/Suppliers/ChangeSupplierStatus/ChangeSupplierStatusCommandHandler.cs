using SupplyGuard.Application.Common.CQRS;
using SupplyGuard.Application.Common.Interfaces;

namespace SupplyGuard.Application.Features.Suppliers.ChangeSupplierStatus;

public sealed class ChangeSupplierStatusCommandHandler(
    ISupplierRepository supplierRepository,
    ICurrentUserService currentUserService) : ICommandHandler<ChangeSupplierStatusCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(
        ChangeSupplierStatusCommand command,
        CancellationToken cancellationToken = default)
    {
        var supplier = await supplierRepository.GetByIdAsync(command.SupplierId, cancellationToken);
        if (supplier is null)
        {
            return Result<Guid>.Failure(new Error("Supplier.NotFound", "The supplier was not found."));
        }

        supplier.ChangeStatus(command.Status, currentUserService.UserId);
        await supplierRepository.SaveChangesAsync(cancellationToken);

        return Result<Guid>.Success(supplier.Id);
    }
}
