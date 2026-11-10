using System.Text.Json.Serialization;

namespace SupplyGuard.Application.Common.Messaging.RiskAnalysis.Jit.V1;

/// <summary>
/// The body contains exactly the worker's three fields. Delivery metadata is retained
/// in the outbox columns and AMQP properties, and restored by the dispatcher on retry.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
[JsonNumberHandling(JsonNumberHandling.Strict)]
public sealed record JitRiskAnalysisRequestedEvent(
    [property: JsonRequired, JsonPropertyName("SupplierId")] Guid SupplierId,
    [property: JsonRequired, JsonPropertyName("CorrelationId")] string CorrelationId,
    [property: JsonRequired, JsonPropertyName("Features")] SupplierRiskFeatures Features) : IIntegrationMessage
{
    public const string TypeName = "risk-analysis.jit-requested.v1";

    [JsonIgnore] public Guid MessageId { get; init; }
    [JsonIgnore] public string MessageType => TypeName;
    [JsonIgnore] public DateTimeOffset OccurredAtUtc { get; init; }
}
