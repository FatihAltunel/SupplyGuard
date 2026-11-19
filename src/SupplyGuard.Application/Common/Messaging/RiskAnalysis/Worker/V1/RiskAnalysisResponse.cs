using System.Text.Json.Serialization;

namespace SupplyGuard.Application.Common.Messaging.RiskAnalysis.Worker.V1;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
[JsonNumberHandling(JsonNumberHandling.Strict)]
public sealed record RiskAnalysisResponse(
    [property: JsonRequired, JsonPropertyName("CorrelationId")] string CorrelationId,
    [property: JsonRequired, JsonPropertyName("SupplierId")] Guid SupplierId,
    [property: JsonRequired, JsonPropertyName("ConfidenceScore")] decimal ConfidenceScore,
    [property: JsonRequired, JsonPropertyName("ExplanationText")] string ExplanationText,
    [property: JsonRequired, JsonPropertyName("IsSuccessful")] bool IsSuccessful,
    [property: JsonPropertyName("ErrorMessage")] string? ErrorMessage = null);
