using FluentValidation;

namespace SupplyGuard.Application.Common.Messaging.RiskAnalysis.Worker.V1;

public sealed class RiskAnalysisRequestValidator : AbstractValidator<RiskAnalysisRequest>
{
    public RiskAnalysisRequestValidator()
    {
        RuleFor(x => x.CorrelationId).NotEmpty().MaximumLength(128)
            .Must(x => x is not null && x == x.Trim());
        RuleFor(x => x.SupplierId).NotEmpty();
        RuleFor(x => x.AverageDeliveryDelayDays).GreaterThanOrEqualTo(0);
        RuleFor(x => x.OnTimeDeliveryRate).InclusiveBetween(0, 1);
        RuleFor(x => x.QualityDefectRate).InclusiveBetween(0, 1);
        RuleFor(x => x.DeliveryDelayTrendPercentage).GreaterThanOrEqualTo(-100);
        RuleFor(x => x.FinancialStabilityScore).InclusiveBetween(0, 100);
        RuleFor(x => x.ActiveRiskIndicatorsCount).GreaterThanOrEqualTo(0);
    }
}

public sealed class RiskAnalysisResponseValidator : AbstractValidator<RiskAnalysisResponse>
{
    public RiskAnalysisResponseValidator()
    {
        RuleFor(x => x.CorrelationId).NotEmpty().MaximumLength(128)
            .Must(x => x is not null && x == x.Trim());
        RuleFor(x => x.SupplierId).NotEmpty();
        RuleFor(x => x.ConfidenceScore).InclusiveBetween(0, 1);
        RuleFor(x => x.ExplanationText).NotNull().MaximumLength(100_000);
        RuleFor(x => x.ErrorMessage).MaximumLength(2_000);
        When(x => x.IsSuccessful, () =>
        {
            RuleFor(x => x.ExplanationText).NotEmpty();
            RuleFor(x => x.ErrorMessage).Null();
        });
        When(x => !x.IsSuccessful, () =>
        {
            RuleFor(x => x.ErrorMessage).NotEmpty();
            RuleFor(x => x.ConfidenceScore).Equal(0);
            RuleFor(x => x.ExplanationText).Equal(string.Empty);
        });
    }
}
