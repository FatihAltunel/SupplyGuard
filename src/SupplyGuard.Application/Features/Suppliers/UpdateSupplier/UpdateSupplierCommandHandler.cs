using SupplyGuard.Application.Common.CQRS;
using SupplyGuard.Application.Common.Interfaces;

namespace SupplyGuard.Application.Features.Suppliers.UpdateSupplier;

public sealed class UpdateSupplierCommandHandler(
    ISupplierRepository supplierRepository,
    ICurrentUserService currentUserService) : ICommandHandler<UpdateSupplierCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(
        UpdateSupplierCommand command,
        CancellationToken cancellationToken = default)
    {
        var supplier = await supplierRepository.GetByIdAsync(command.SupplierId, cancellationToken);
        if (supplier is null)
        {
            return Result<Guid>.Failure(new Error("Supplier.NotFound", "The supplier was not found."));
        }

        supplier.UpdateProfile(
            command.RegistrationNumber,
            command.City,
            command.Address,
            command.Industry,
            command.SupplierCategory,
            currentUserService.UserId);
        supplier.UpdateContactDetails(
            command.ContactName,
            command.ContactEmail,
            command.ContactPhone,
            command.WebsiteUrl,
            currentUserService.UserId);
        supplier.SetCriticality(command.IsCriticalSupplier, currentUserService.UserId);

        if (command.OnboardingDateUtc is { } onboardingDateUtc)
        {
            supplier.SetOnboardingDate(onboardingDateUtc, currentUserService.UserId);
        }

        await supplierRepository.SaveChangesAsync(cancellationToken);
        return Result<Guid>.Success(supplier.Id);
    }
}
