using System.Text.Json.Serialization;

namespace SupplyGuard.Application.Common.Messaging.RiskAnalysis.V1;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
[JsonNumberHandling(JsonNumberHandling.Strict)]
public sealed record RiskAnalysisRequestedEvent(
    [property: JsonRequired, JsonPropertyName("messageId")] Guid MessageId,
    [property: JsonRequired, JsonPropertyName("messageType")] string MessageType,
    [property: JsonRequired, JsonPropertyName("correlationId")] Guid CorrelationId,
    [property: JsonRequired, JsonPropertyName("occurredAtUtc")] DateTimeOffset OccurredAtUtc,
    [property: JsonRequired, JsonPropertyName("supplierId")] Guid SupplierId,
    [property: JsonRequired, JsonPropertyName("input")] RiskAnalysisInputV1 Input) : IIntegrationMessage
{
    public const string TypeName = "risk-analysis.requested.v1";

    string IIntegrationMessage.CorrelationId => CorrelationId.ToString("D");
}
