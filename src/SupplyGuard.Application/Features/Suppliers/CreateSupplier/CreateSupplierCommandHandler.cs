using SupplyGuard.Application.Common.CQRS;
using SupplyGuard.Application.Common.Interfaces;
using SupplyGuard.Domain.Entities;

namespace SupplyGuard.Application.Features.Suppliers.CreateSupplier;

public sealed class CreateSupplierCommandHandler(
    ISupplierRepository supplierRepository,
    ICurrentUserService currentUserService) : ICommandHandler<CreateSupplierCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(
        CreateSupplierCommand command,
        CancellationToken cancellationToken = default)
    {
        if (await supplierRepository.ExistsByBusinessKeyAsync(
                command.CountryCode.Trim().ToUpperInvariant(),
                command.TaxNumber.Trim().ToUpperInvariant(),
                cancellationToken))
        {
            return Result<Guid>.Failure(new Error(
                "Supplier.BusinessKeyAlreadyExists",
                "A supplier with the same country code and tax number already exists."));
        }

        var supplier = new Supplier(
            command.Name,
            command.TaxNumber,
            command.CountryCode,
            currentUserService.UserId);

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

        await supplierRepository.AddAsync(supplier, cancellationToken);
        await supplierRepository.SaveChangesAsync(cancellationToken);

        return Result<Guid>.Success(supplier.Id);
    }
}
