using System.Text.Json.Serialization;

namespace SupplyGuard.Application.Common.Messaging.RiskAnalysis.V1;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
[JsonNumberHandling(JsonNumberHandling.Strict)]
public sealed record RiskAnalysisCompletedEvent(
    [property: JsonRequired, JsonPropertyName("messageId")] Guid MessageId,
    [property: JsonRequired, JsonPropertyName("messageType")] string MessageType,
    [property: JsonRequired, JsonPropertyName("correlationId")] Guid CorrelationId,
    [property: JsonRequired, JsonPropertyName("occurredAtUtc")] DateTimeOffset OccurredAtUtc,
    [property: JsonRequired, JsonPropertyName("supplierId")] Guid SupplierId,
    [property: JsonRequired, JsonPropertyName("requestMessageId")] Guid RequestMessageId,
    [property: JsonRequired, JsonPropertyName("status")] string Status,
    [property: JsonRequired, JsonPropertyName("model")] RiskAnalysisModelV1 Model,
    [property: JsonRequired, JsonPropertyName("execution")] RiskAnalysisExecutionV1 Execution,
    [property: JsonRequired, JsonPropertyName("result")] RiskAnalysisResultV1? Result,
    [property: JsonRequired, JsonPropertyName("error")] RiskAnalysisErrorV1? Error) : IIntegrationMessage
{
    public const string TypeName = "risk-analysis.completed.v1";

    string IIntegrationMessage.CorrelationId => CorrelationId.ToString("D");
}
