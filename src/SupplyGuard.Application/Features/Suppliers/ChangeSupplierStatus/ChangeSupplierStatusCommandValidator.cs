using FluentValidation;

namespace SupplyGuard.Application.Features.Suppliers.ChangeSupplierStatus;

public sealed class ChangeSupplierStatusCommandValidator : AbstractValidator<ChangeSupplierStatusCommand>
{
    public ChangeSupplierStatusCommandValidator()
    {
        RuleFor(command => command.SupplierId).NotEmpty();
        RuleFor(command => command.Status).IsInEnum();
    }
}
