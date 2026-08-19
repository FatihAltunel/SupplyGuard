using SupplyGuard.Application.Common.CQRS;
using SupplyGuard.Application.Common.Interfaces;

namespace SupplyGuard.Application.Features.RiskManagement.AcknowledgeEarlyWarning;

public sealed class AcknowledgeEarlyWarningCommandHandler(
    IRiskManagementRepository riskManagementRepository,
    ICurrentUserService currentUserService,
    TimeProvider timeProvider)
    : ICommandHandler<AcknowledgeEarlyWarningCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(
        AcknowledgeEarlyWarningCommand command,
        CancellationToken cancellationToken = default)
    {
        var supplier = await riskManagementRepository.GetSupplierWithWarningsAsync(
            command.SupplierId,
            cancellationToken);
        if (supplier is null)
        {
            return Result<Guid>.Failure(new Error("Supplier.NotFound", "The supplier was not found."));
        }

        if (supplier.EarlyWarnings.All(warning => warning.Id != command.EarlyWarningId))
        {
            return Result<Guid>.Failure(new Error(
                "EarlyWarning.NotFound",
                "The early warning was not found for this supplier."));
        }

        if (currentUserService.UserId is not { } userId)
        {
            return Result<Guid>.Failure(new Error(
                "User.NotAuthenticated",
                "An authenticated user is required to acknowledge an early warning."));
        }

        try
        {
            supplier.AcknowledgeEarlyWarning(
                command.EarlyWarningId,
                userId,
                timeProvider.GetUtcNow());
        }
        catch (InvalidOperationException exception)
        {
            return Result<Guid>.Failure(new Error("EarlyWarning.InvalidTransition", exception.Message));
        }

        await riskManagementRepository.SaveChangesAsync(cancellationToken);
        supplier.ClearDomainEvents();

        return Result<Guid>.Success(command.EarlyWarningId);
    }
}
