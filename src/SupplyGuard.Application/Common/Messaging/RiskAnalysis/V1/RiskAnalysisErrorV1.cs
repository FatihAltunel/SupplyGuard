using System.Text.Json.Serialization;

namespace SupplyGuard.Application.Common.Messaging.RiskAnalysis.V1;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
[JsonNumberHandling(JsonNumberHandling.Strict)]
public sealed record RiskAnalysisErrorV1(
    [property: JsonRequired, JsonPropertyName("code")] string Code,
    [property: JsonRequired, JsonPropertyName("message")] string Message);
