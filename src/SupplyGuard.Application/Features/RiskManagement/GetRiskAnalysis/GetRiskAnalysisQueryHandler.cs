using System.Text.Json;
using SupplyGuard.Application.Common.CQRS;
using SupplyGuard.Application.Common.Interfaces;
using SupplyGuard.Application.Common.Messaging.RiskAnalysis.Worker.V1;
using SupplyGuard.Domain.Enums;

namespace SupplyGuard.Application.Features.RiskManagement.GetRiskAnalysis;

public sealed class GetRiskAnalysisQueryHandler(
    IXAIAuditLogRepository audits,
    IIntegrationMessageSerializer serializer,
    IRiskAnalysisResponseSerializer responseSerializer) : IQueryHandler<GetRiskAnalysisQuery, RiskAnalysisStatusDto?>
{
    // Worker wire names (supplyguard-ml-worker/schemas.py FEATURE_NAMES) -> the API's camelCase feature keys.
    private static readonly IReadOnlyDictionary<string, string> FeatureKeys = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["averageDeliveryDelayDays"] = "averageDeliveryDelayDays",
        ["onTimeDeliveryRate"] = "onTimeDeliveryRate",
        ["qualityDefectRate"] = "qualityDefectRate",
        ["DeliveryDelayTrend_Percentage"] = "deliveryDelayTrendPercentage",
        ["FinancialStabilityScore"] = "financialStabilityScore",
        ["ActiveRiskIndicatorsCount"] = "activeRiskIndicatorsCount"
    };

    public async Task<RiskAnalysisStatusDto?> HandleAsync(GetRiskAnalysisQuery query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query.CorrelationId) || query.CorrelationId.Length > 128) return null;

        var audit = await audits.GetByCorrelationIdAsync(query.CorrelationId, cancellationToken);
        // A correlation owned by another supplier is reported as missing, not as a mismatch.
        if (audit is null || audit.SupplierId != query.SupplierId) return null;

        var completed = audit.ExplanationStatus == ExplanationStatus.Completed;
        var failed = audit.ExplanationStatus == ExplanationStatus.Failed;
        return new RiskAnalysisStatusDto(
            audit.SupplierId,
            audit.CorrelationId,
            audit.ExplanationStatus.ToString(),
            completed ? audit.ConfidenceScore : null,
            completed ? audit.ExplanationText : null,
            failed ? audit.FailureMessage : null,
            audit.RequestedAtUtc,
            audit.CompletedAtUtc,
            ReadFeatures(audit.RequestPayload),
            completed ? ReadExplanation(audit.ResponsePayload) : null);
    }

    private RiskAnalysisFeaturesDto? ReadFeatures(string requestPayload)
    {
        try
        {
            var request = serializer.Deserialize<RiskAnalysisRequest>(requestPayload);
            return new RiskAnalysisFeaturesDto(
                request.AverageDeliveryDelayDays, request.OnTimeDeliveryRate, request.QualityDefectRate,
                request.DeliveryDelayTrendPercentage, request.FinancialStabilityScore, request.ActiveRiskIndicatorsCount);
        }
        catch (JsonException)
        {
            // Legacy v1 audits retain a different request contract; their inputs are not exposed here.
            return null;
        }
    }

    private RiskAnalysisExplanationDto? ReadExplanation(string? responsePayload)
    {
        if (responsePayload is null) return null;
        try
        {
            var response = responseSerializer.Deserialize(responsePayload);
            if (response.BaseValue is not { } baseValue || response.Contributions is null) return null;
            return new RiskAnalysisExplanationDto(baseValue, response.Contributions
                .Select(item => new RiskAnalysisContributionDto(
                    FeatureKeys.TryGetValue(item.Feature, out var key) ? key : item.Feature, item.Contribution))
                .ToArray());
        }
        catch (JsonException)
        {
            // Legacy v1 completions and older worker responses carry no SHAP decomposition.
            return null;
        }
    }
}
