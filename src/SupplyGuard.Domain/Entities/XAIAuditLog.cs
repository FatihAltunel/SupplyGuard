using SupplyGuard.Domain.Common;

namespace SupplyGuard.Domain.Entities;

public class XAIAuditLog : BaseEntity
{
    public Guid SupplierId { get; private set; }
    public Guid? RiskAssessmentId { get; private set; }
    public string ModelName { get; private set; } = null!;
    public string ModelVersion { get; private set; } = null!;
    public string CorrelationId { get; private set; } = null!;
    public string RequestPayload { get; private set; } = null!;
    public string? ResponsePayload { get; private set; }
    public decimal ConfidenceScore { get; private set; }
    public int LatencyMs { get; private set; }
    public bool IsSuccessful { get; private set; }
    public string? FailureCode { get; private set; }
    public string? FailureMessage { get; private set; }
    public DateTimeOffset ExecutedAtUtc { get; private set; }
    public Supplier Supplier { get; private set; } = null!;
    public RiskAssessment? RiskAssessment { get; private set; }

    private XAIAuditLog()
    {
        // Required by EF Core.
    }

    public XAIAuditLog(
        Guid supplierId,
        string modelName,
        string modelVersion,
        string correlationId,
        string requestPayload,
        string? responsePayload,
        decimal confidenceScore,
        int latencyMs,
        bool isSuccessful,
        DateTimeOffset executedAtUtc,
        Guid? riskAssessmentId = null,
        string? failureCode = null,
        string? failureMessage = null)
        : base(Guid.NewGuid())
    {
        SupplierId = RequireId(supplierId, nameof(supplierId));
        RiskAssessmentId = riskAssessmentId is null ? null : RequireId(riskAssessmentId.Value, nameof(riskAssessmentId));
        ModelName = RequireText(modelName, nameof(modelName), 200);
        ModelVersion = RequireText(modelVersion, nameof(modelVersion), 100);
        CorrelationId = RequireText(correlationId, nameof(correlationId), 128);
        RequestPayload = RequireText(requestPayload, nameof(requestPayload), 100_000);
        ResponsePayload = NormalizeOptionalText(responsePayload, 100_000);
        ConfidenceScore = RequireConfidenceScore(confidenceScore);
        LatencyMs = latencyMs >= 0 ? latencyMs : throw new ArgumentOutOfRangeException(nameof(latencyMs), "Latency cannot be negative.");
        IsSuccessful = isSuccessful;
        FailureCode = NormalizeOptionalText(failureCode, 100);
        FailureMessage = NormalizeOptionalText(failureMessage, 2_000);
        ExecutedAtUtc = executedAtUtc.ToUniversalTime();

        if (isSuccessful && ResponsePayload is null)
        {
            throw new ArgumentException("A successful XAI request requires a response payload.", nameof(responsePayload));
        }

        if (!isSuccessful && FailureCode is null)
        {
            throw new ArgumentException("A failed XAI request requires a failure code.", nameof(failureCode));
        }
    }

    private static Guid RequireId(Guid value, string parameterName) => value == Guid.Empty ? throw new ArgumentException("Identifier cannot be empty.", parameterName) : value;
    private static decimal RequireConfidenceScore(decimal value) => value is < 0 or > 1 ? throw new ArgumentOutOfRangeException(nameof(value), "Confidence score must be between 0 and 1.") : value;
    private static string RequireText(string value, string parameterName, int maximumLength)
    {
        var normalizedValue = value?.Trim();
        return string.IsNullOrWhiteSpace(normalizedValue) || normalizedValue.Length > maximumLength
            ? throw new ArgumentException($"Value must contain between 1 and {maximumLength} characters.", parameterName)
            : normalizedValue;
    }
    private static string? NormalizeOptionalText(string? value, int maximumLength) => string.IsNullOrWhiteSpace(value) ? null : RequireText(value, nameof(value), maximumLength);
}
