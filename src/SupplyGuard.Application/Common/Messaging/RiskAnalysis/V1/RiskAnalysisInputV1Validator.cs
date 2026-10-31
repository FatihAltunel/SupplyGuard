using FluentValidation;

namespace SupplyGuard.Application.Common.Messaging.RiskAnalysis.V1;

public sealed class RiskAnalysisInputV1Validator : AbstractValidator<RiskAnalysisInputV1>
{
    public RiskAnalysisInputV1Validator()
    {
        RuleFor(input => input.FeatureSchemaVersion).Equal(1);
        RuleFor(input => input.ObservedAtUtc).Must(RiskAnalysisContractRules.IsUtcMilliseconds)
            .WithMessage("Observation time must be UTC with millisecond precision.");
        RuleFor(input => input.Features).NotNull().NotEmpty();
        RuleForEach(input => input.Features).Must(feature =>
                !string.IsNullOrWhiteSpace(feature.Key) &&
                (feature.Value is null || double.IsFinite(feature.Value.Value)))
            .WithMessage("Features require non-empty names and finite numbers or explicit nulls.");
    }
}
