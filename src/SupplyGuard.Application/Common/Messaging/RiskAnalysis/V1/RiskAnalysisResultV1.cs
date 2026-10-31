using System.Text.Json.Serialization;

namespace SupplyGuard.Application.Common.Messaging.RiskAnalysis.V1;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
[JsonNumberHandling(JsonNumberHandling.Strict)]
public sealed record RiskAnalysisResultV1(
    [property: JsonRequired, JsonPropertyName("riskScore")] decimal RiskScore,
    [property: JsonRequired, JsonPropertyName("confidenceScore")] decimal ConfidenceScore,
    [property: JsonRequired, JsonPropertyName("explanation")] RiskAnalysisExplanationV1 Explanation);
