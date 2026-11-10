using FluentValidation;

namespace SupplyGuard.Application.Common.Messaging.RiskAnalysis.Jit.V1;

/// <summary>Validates the JSON body; delivery metadata lives outside that body.</summary>
public sealed class JitRiskAnalysisRequestedEventValidator : AbstractValidator<JitRiskAnalysisRequestedEvent>
{
    public JitRiskAnalysisRequestedEventValidator()
    {
        RuleFor(message => message.SupplierId).NotEmpty();
        RuleFor(message => message.CorrelationId).NotEmpty().MaximumLength(OutboxMessage.CorrelationIdMaxLength);
        RuleFor(message => message.Features).NotNull().SetValidator(new SupplierRiskFeaturesValidator());
    }
}

public sealed class SupplierRiskFeaturesValidator : AbstractValidator<SupplierRiskFeatures>
{
    public SupplierRiskFeaturesValidator()
    {
        RuleFor(features => features.AverageDeliveryDelayDays).GreaterThanOrEqualTo(0);
        RuleFor(features => features.OnTimeDeliveryRate).InclusiveBetween(0, 1);
        RuleFor(features => features.QualityDefectRate).InclusiveBetween(0, 1);
        RuleFor(features => features.DeliveryDelayTrendPercentage).GreaterThanOrEqualTo(-100);
        RuleFor(features => features.FinancialStabilityScore).InclusiveBetween(0, 100);
        RuleFor(features => features.ActiveRiskIndicatorsCount).GreaterThanOrEqualTo(0);
    }
}
