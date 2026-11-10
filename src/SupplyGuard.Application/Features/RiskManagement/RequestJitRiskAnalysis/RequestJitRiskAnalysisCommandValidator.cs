using FluentValidation;
using SupplyGuard.Application.Common.Messaging;

namespace SupplyGuard.Application.Features.RiskManagement.RequestJitRiskAnalysis;

public sealed class RequestJitRiskAnalysisCommandValidator : AbstractValidator<RequestJitRiskAnalysisCommand>
{
    public RequestJitRiskAnalysisCommandValidator()
    {
        RuleFor(command => command.SupplierId).NotEmpty();
        RuleFor(command => command.CorrelationId).NotEmpty().MaximumLength(OutboxMessage.CorrelationIdMaxLength);
        RuleFor(command => command.AverageDeliveryDelayDays).GreaterThanOrEqualTo(0);
        RuleFor(command => command.OnTimeDeliveryRate).InclusiveBetween(0, 1);
        RuleFor(command => command.QualityDefectRate).InclusiveBetween(0, 1);
    }
}
