using System.Text.Json.Serialization;

namespace SupplyGuard.Application.Common.Messaging.RiskAnalysis.Jit.V1;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
[JsonNumberHandling(JsonNumberHandling.Strict)]
public sealed record SupplierRiskFeatures(
    [property: JsonRequired, JsonPropertyName("AverageDeliveryDelayDays")] decimal AverageDeliveryDelayDays,
    [property: JsonRequired, JsonPropertyName("OnTimeDeliveryRate")] decimal OnTimeDeliveryRate,
    [property: JsonRequired, JsonPropertyName("QualityDefectRate")] decimal QualityDefectRate,
    [property: JsonRequired, JsonPropertyName("DeliveryDelayTrend_Percentage")] decimal DeliveryDelayTrendPercentage,
    [property: JsonRequired, JsonPropertyName("FinancialStabilityScore")] decimal FinancialStabilityScore,
    [property: JsonRequired, JsonPropertyName("ActiveRiskIndicatorsCount")] int ActiveRiskIndicatorsCount);
