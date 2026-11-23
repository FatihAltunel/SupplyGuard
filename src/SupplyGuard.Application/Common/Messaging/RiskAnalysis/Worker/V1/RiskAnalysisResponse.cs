using System.Text.Json.Serialization;

namespace SupplyGuard.Application.Common.Messaging.RiskAnalysis.Worker.V1;

/// <summary>
/// The flat response published by the Python worker (supplyguard-ml-worker/schemas.py).
/// BaseValue and Contributions are optional for compatibility with workers that predate them.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
[JsonNumberHandling(JsonNumberHandling.Strict)]
public sealed record RiskAnalysisResponse(
    [property: JsonRequired, JsonPropertyName("CorrelationId")] string CorrelationId,
    [property: JsonRequired, JsonPropertyName("SupplierId")] Guid SupplierId,
    [property: JsonRequired, JsonPropertyName("ConfidenceScore")] decimal ConfidenceScore,
    [property: JsonRequired, JsonPropertyName("ExplanationText")] string ExplanationText,
    [property: JsonRequired, JsonPropertyName("IsSuccessful")] bool IsSuccessful,
    [property: JsonPropertyName("ErrorMessage")] string? ErrorMessage = null,
    [property: JsonPropertyName("BaseValue")] decimal? BaseValue = null,
    [property: JsonPropertyName("Contributions")] IReadOnlyList<RiskAnalysisFeatureContribution>? Contributions = null)
{
    // Structural equality: duplicate deliveries are detected by comparing a redelivered response with
    // the retained one, so the contribution list must compare by content, not by reference.
    public bool Equals(RiskAnalysisResponse? other) =>
        other is not null &&
        CorrelationId == other.CorrelationId &&
        SupplierId == other.SupplierId &&
        ConfidenceScore == other.ConfidenceScore &&
        ExplanationText == other.ExplanationText &&
        IsSuccessful == other.IsSuccessful &&
        ErrorMessage == other.ErrorMessage &&
        BaseValue == other.BaseValue &&
        (Contributions is null
            ? other.Contributions is null
            : other.Contributions is not null && Contributions.SequenceEqual(other.Contributions));

    public override int GetHashCode() => HashCode.Combine(
        CorrelationId, SupplierId, ConfidenceScore, ExplanationText, IsSuccessful, ErrorMessage, BaseValue, Contributions?.Count);
}

/// <summary>One SHAP value for the risk class, in LightGBM raw-score (log-odds) units.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
[JsonNumberHandling(JsonNumberHandling.Strict)]
public sealed record RiskAnalysisFeatureContribution(
    [property: JsonRequired, JsonPropertyName("Feature")] string Feature,
    [property: JsonRequired, JsonPropertyName("Contribution")] decimal Contribution);
