using FluentValidation;
using SupplyGuard.Application.Common.Messaging.RiskAnalysis.V1;

namespace SupplyGuard.Application.Features.RiskManagement.RequestRiskAnalysis;

public sealed class RequestRiskAnalysisCommandValidator : AbstractValidator<RequestRiskAnalysisCommand>
{
    public RequestRiskAnalysisCommandValidator(IValidator<RiskAnalysisInputV1> inputValidator)
    {
        RuleFor(command => command.SupplierId).NotEmpty();
        RuleFor(command => command.Input).NotNull().SetValidator(inputValidator);
    }
}
