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
            RuleFor(x => x.BaseValue).Null();
            RuleFor(x => x.Contributions).Null();
        });
        // The SHAP decomposition is all-or-nothing: a base value without contributions (or vice versa) is invalid.
        RuleFor(x => x.BaseValue).NotNull().When(x => x.Contributions is not null);
        RuleFor(x => x.Contributions).NotNull().When(x => x.BaseValue is not null);
        RuleFor(x => x.Contributions!)
            .Cascade(CascadeMode.Stop)
            .Must(items => items.All(item => item is not null))
            .WithMessage("Contributions cannot contain null items.")
            .Must(items => items.Count is > 0 and <= 32)
            .WithMessage("Contributions must contain between 1 and 32 features.")
            .Must(items => items.Select(item => item.Feature).Distinct(StringComparer.Ordinal).Count() == items.Count)
            .WithMessage("Contribution features must be unique.")
            .When(x => x.Contributions is not null);
        RuleForEach(x => x.Contributions).ChildRules(item =>
        {
            item.RuleFor(contribution => contribution.Feature).NotEmpty().MaximumLength(100)
                .Must(feature => feature is not null && feature == feature.Trim());
        }).When(x => x.Contributions is not null && x.Contributions.All(item => item is not null));
    }
}
