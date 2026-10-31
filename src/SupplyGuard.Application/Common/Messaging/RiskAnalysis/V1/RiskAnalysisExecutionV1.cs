using System.Text.Json.Serialization;

namespace SupplyGuard.Application.Common.Messaging.RiskAnalysis.V1;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
[JsonNumberHandling(JsonNumberHandling.Strict)]
public sealed record RiskAnalysisExecutionV1(
    [property: JsonRequired, JsonPropertyName("startedAtUtc")] DateTimeOffset StartedAtUtc,
    [property: JsonRequired, JsonPropertyName("completedAtUtc")] DateTimeOffset CompletedAtUtc,
    [property: JsonRequired, JsonPropertyName("latencyMs")] int LatencyMs);
