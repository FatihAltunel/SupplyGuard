using FluentValidation;
using SupplyGuard.Application.Common.Messaging;

namespace SupplyGuard.Application.Features.RiskManagement.RequestJitRiskAnalysis;

public sealed class RequestJitRiskAnalysisCommandValidator : AbstractValidator<RequestJitRiskAnalysisCommand>
{
    public RequestJitRiskAnalysisCommandValidator()
    {
        RuleFor(command => command.SupplierId).NotEmpty();
        RuleFor(command => command.CorrelationId).NotEmpty().MaximumLength(OutboxMessage.CorrelationIdMaxLength)
            .Must(value => value is not null && value == value.Trim());
        RuleFor(command => command.AverageDeliveryDelayDays).GreaterThanOrEqualTo(0);
        RuleFor(command => command.OnTimeDeliveryRate).InclusiveBetween(0, 1);
        RuleFor(command => command.QualityDefectRate).InclusiveBetween(0, 1);
        // Same ranges as the worker contract validator.
        RuleFor(command => command.Overrides!.DeliveryDelayTrendPercentage).GreaterThanOrEqualTo(-100)
            .When(command => command.Overrides?.DeliveryDelayTrendPercentage is not null);
        RuleFor(command => command.Overrides!.FinancialStabilityScore).InclusiveBetween(0, 100)
            .When(command => command.Overrides?.FinancialStabilityScore is not null);
        RuleFor(command => command.Overrides!.ActiveRiskIndicatorsCount).GreaterThanOrEqualTo(0)
            .When(command => command.Overrides?.ActiveRiskIndicatorsCount is not null);
    }
}
