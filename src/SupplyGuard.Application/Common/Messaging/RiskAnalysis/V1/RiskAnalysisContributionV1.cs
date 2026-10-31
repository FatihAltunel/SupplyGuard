using System.Text.Json.Serialization;

namespace SupplyGuard.Application.Common.Messaging.RiskAnalysis.V1;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
[JsonNumberHandling(JsonNumberHandling.Strict)]
public sealed record RiskAnalysisContributionV1(
    [property: JsonRequired, JsonPropertyName("feature")] string Feature,
    [property: JsonRequired, JsonPropertyName("contribution")] decimal Contribution);
