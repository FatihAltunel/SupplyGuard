using System.Text.Json;
using SupplyGuard.Application.Common.CQRS;
using SupplyGuard.Application.Common.Interfaces;
using SupplyGuard.Application.Common.Messaging.RiskAnalysis.Worker.V1;
using SupplyGuard.Domain.Enums;

namespace SupplyGuard.Application.Features.RiskManagement.GetRiskAnalysis;

public sealed class GetRiskAnalysisQueryHandler(
    IXAIAuditLogRepository audits,
    IIntegrationMessageSerializer serializer) : IQueryHandler<GetRiskAnalysisQuery, RiskAnalysisStatusDto?>
{
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
            ReadFeatures(audit.RequestPayload));
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
}
