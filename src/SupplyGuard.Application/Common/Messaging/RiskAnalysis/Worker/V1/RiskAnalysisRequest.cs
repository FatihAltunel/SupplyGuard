using System.Text.Json.Serialization;

namespace SupplyGuard.Application.Common.Messaging.RiskAnalysis.Worker.V1;

/// <summary>The exact flat contract consumed by the Python worker. Metadata is never part of its body.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
[JsonNumberHandling(JsonNumberHandling.Strict)]
public sealed record RiskAnalysisRequest(
    [property: JsonRequired, JsonPropertyName("CorrelationId")] string CorrelationId,
    [property: JsonRequired, JsonPropertyName("SupplierId")] Guid SupplierId,
    [property: JsonRequired, JsonPropertyName("averageDeliveryDelayDays")] decimal AverageDeliveryDelayDays,
    [property: JsonRequired, JsonPropertyName("onTimeDeliveryRate")] decimal OnTimeDeliveryRate,
    [property: JsonRequired, JsonPropertyName("qualityDefectRate")] decimal QualityDefectRate,
    [property: JsonRequired, JsonPropertyName("DeliveryDelayTrend_Percentage")] decimal DeliveryDelayTrendPercentage,
    [property: JsonRequired, JsonPropertyName("FinancialStabilityScore")] decimal FinancialStabilityScore,
    [property: JsonRequired, JsonPropertyName("ActiveRiskIndicatorsCount")] int ActiveRiskIndicatorsCount) : IIntegrationMessage
{
    public const string TypeName = "risk-analysis.worker-requested.v1";
    [JsonIgnore] public Guid MessageId { get; init; }
    [JsonIgnore] public string MessageType => TypeName;
    [JsonIgnore] public DateTimeOffset OccurredAtUtc { get; init; }
}
