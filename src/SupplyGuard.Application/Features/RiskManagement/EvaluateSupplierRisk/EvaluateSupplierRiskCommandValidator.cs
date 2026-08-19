using FluentValidation;

namespace SupplyGuard.Application.Features.RiskManagement.EvaluateSupplierRisk;

public sealed class EvaluateSupplierRiskCommandValidator : AbstractValidator<EvaluateSupplierRiskCommand>
{
    public EvaluateSupplierRiskCommandValidator()
    {
        RuleFor(command => command.SupplierId).NotEmpty();
        RuleFor(command => command.CorrelationId).NotEmpty().MaximumLength(128);
    }
}
