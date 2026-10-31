using FluentValidation;

namespace SupplyGuard.Application.Common.Messaging.RiskAnalysis.V1;

public sealed class RiskAnalysisCompletedEventValidator : AbstractValidator<RiskAnalysisCompletedEvent>
{
    public RiskAnalysisCompletedEventValidator()
    {
        RuleFor(message => message.MessageId).NotEmpty();
        RuleFor(message => message.MessageType).Equal(RiskAnalysisCompletedEvent.TypeName);
        RuleFor(message => message.CorrelationId).NotEmpty();
        RuleFor(message => message.SupplierId).NotEmpty();
        RuleFor(message => message.RequestMessageId).NotEmpty();
        RuleFor(message => message.OccurredAtUtc).Must(RiskAnalysisContractRules.IsUtcMilliseconds);
        RuleFor(message => message.Status).Must(status =>
            status is RiskAnalysisContractRules.Succeeded or RiskAnalysisContractRules.Failed);
        RuleFor(message => message.Model).NotNull();
        RuleFor(message => message.Execution).NotNull();

        When(message => message.Model is not null, () =>
        {
            RuleFor(message => message.Model.Name).Must(value => RiskAnalysisContractRules.HasText(value, 200));
            RuleFor(message => message.Model.Version).Must(value => RiskAnalysisContractRules.HasText(value, 100));
        });
        When(message => message.Execution is not null, () =>
        {
            RuleFor(message => message.Execution.StartedAtUtc).Must(RiskAnalysisContractRules.IsUtcMilliseconds);
            RuleFor(message => message.Execution.CompletedAtUtc).Must(RiskAnalysisContractRules.IsUtcMilliseconds)
                .GreaterThanOrEqualTo(message => message.Execution.StartedAtUtc)
                .Equal(message => message.OccurredAtUtc);
            RuleFor(message => message.Execution.LatencyMs).GreaterThanOrEqualTo(0);
        });
        When(message => message.Status == RiskAnalysisContractRules.Succeeded, () =>
        {
            RuleFor(message => message.Result).NotNull();
            RuleFor(message => message.Error).Null();
        });
        When(message => message.Status == RiskAnalysisContractRules.Failed, () =>
        {
            RuleFor(message => message.Result).Null();
            RuleFor(message => message.Error).NotNull();
        });
        When(message => message.Result is not null, () =>
        {
            RuleFor(message => message.Result!.RiskScore).InclusiveBetween(0m, 100m)
                .Must(value => decimal.Round(value, 2) == value);
            RuleFor(message => message.Result!.ConfidenceScore).InclusiveBetween(0m, 1m)
                .Must(value => decimal.Round(value, 4) == value);
            RuleFor(message => message.Result!.Explanation).NotNull();
            RuleFor(message => message.Result!).Must(RiskAnalysisContractRules.ReconstructsScore)
                .WithMessage("Explanation contributions must reconstruct the risk score within 0.000001.");
        });
        When(message => message.Result?.Explanation is not null, () =>
        {
            RuleFor(message => message.Result!.Explanation.Method).Equal("dummy-shap");
            RuleFor(message => message.Result!.Explanation.OutputScale).Equal("risk-score-0-100");
            RuleFor(message => message.Result!.Explanation.Reason)
                .Must(value => RiskAnalysisContractRules.HasText(value, 4000));
            RuleFor(message => message.Result!.Explanation.Contributions).NotNull();
            RuleForEach(message => message.Result!.Explanation.Contributions)
                .Must(item => item is not null && !string.IsNullOrWhiteSpace(item.Feature));
        });
        When(message => message.Error is not null, () =>
        {
            RuleFor(message => message.Error!.Code).Must(value => RiskAnalysisContractRules.HasText(value, 100));
            RuleFor(message => message.Error!.Message).Must(value => RiskAnalysisContractRules.HasText(value, 2000));
        });
    }
}
