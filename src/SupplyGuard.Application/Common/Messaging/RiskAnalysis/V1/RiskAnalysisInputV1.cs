using System.Text.Json.Serialization;

namespace SupplyGuard.Application.Common.Messaging.RiskAnalysis.V1;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
[JsonNumberHandling(JsonNumberHandling.Strict)]
public sealed record RiskAnalysisInputV1(
    [property: JsonRequired, JsonPropertyName("featureSchemaVersion")] int FeatureSchemaVersion,
    [property: JsonRequired, JsonPropertyName("observedAtUtc")] DateTimeOffset ObservedAtUtc,
    [property: JsonRequired, JsonPropertyName("features")] IReadOnlyDictionary<string, double?> Features);
