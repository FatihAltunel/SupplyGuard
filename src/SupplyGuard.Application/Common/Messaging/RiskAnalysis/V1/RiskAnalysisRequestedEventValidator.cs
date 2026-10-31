using FluentValidation;

namespace SupplyGuard.Application.Common.Messaging.RiskAnalysis.V1;

public sealed class RiskAnalysisRequestedEventValidator : AbstractValidator<RiskAnalysisRequestedEvent>
{
    public RiskAnalysisRequestedEventValidator(IValidator<RiskAnalysisInputV1> inputValidator)
    {
        RuleFor(message => message.MessageId).NotEmpty();
        RuleFor(message => message.MessageType).Equal(RiskAnalysisRequestedEvent.TypeName);
        RuleFor(message => message.CorrelationId).NotEmpty();
        RuleFor(message => message.SupplierId).NotEmpty();
        RuleFor(message => message.OccurredAtUtc).Must(RiskAnalysisContractRules.IsUtcMilliseconds);
        RuleFor(message => message.Input).NotNull().SetValidator(inputValidator);
    }
}
