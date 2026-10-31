using FluentValidation;
using SupplyGuard.Application.Common.Messaging.RiskAnalysis.V1;

namespace SupplyGuard.Application.Features.RiskManagement.ProcessRiskAnalysisCompleted;

public sealed class ProcessRiskAnalysisCompletedCommandValidator : AbstractValidator<ProcessRiskAnalysisCompletedCommand>
{
    public ProcessRiskAnalysisCompletedCommandValidator(IValidator<RiskAnalysisCompletedEvent> eventValidator)
    {
        RuleFor(command => command.Completion).NotNull().SetValidator(eventValidator);
    }
}
