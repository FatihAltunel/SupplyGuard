using FluentValidation;

namespace SupplyGuard.Application.Features.RiskManagement.AcknowledgeEarlyWarning;

public sealed class AcknowledgeEarlyWarningCommandValidator : AbstractValidator<AcknowledgeEarlyWarningCommand>
{
    public AcknowledgeEarlyWarningCommandValidator()
    {
        RuleFor(command => command.SupplierId).NotEmpty();
        RuleFor(command => command.EarlyWarningId).NotEmpty();
    }
}
