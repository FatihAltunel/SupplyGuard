using System.Text.Json.Serialization;

namespace SupplyGuard.Application.Common.Messaging.RiskAnalysis.V1;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
[JsonNumberHandling(JsonNumberHandling.Strict)]
public sealed record RiskAnalysisModelV1(
    [property: JsonRequired, JsonPropertyName("name")] string Name,
    [property: JsonRequired, JsonPropertyName("version")] string Version,
    [property: JsonRequired, JsonPropertyName("isMock")] bool IsMock);
