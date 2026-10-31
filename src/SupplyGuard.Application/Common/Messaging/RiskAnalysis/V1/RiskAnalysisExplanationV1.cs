using System.Text.Json.Serialization;

namespace SupplyGuard.Application.Common.Messaging.RiskAnalysis.V1;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
[JsonNumberHandling(JsonNumberHandling.Strict)]
public sealed record RiskAnalysisExplanationV1(
    [property: JsonRequired, JsonPropertyName("method")] string Method,
    [property: JsonRequired, JsonPropertyName("reason")] string Reason,
    [property: JsonRequired, JsonPropertyName("outputScale")] string OutputScale,
    [property: JsonRequired, JsonPropertyName("baseValue")] decimal BaseValue,
    [property: JsonRequired, JsonPropertyName("contributions")] IReadOnlyList<RiskAnalysisContributionV1> Contributions);
