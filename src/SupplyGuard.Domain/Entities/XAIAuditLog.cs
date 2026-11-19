using SupplyGuard.Domain.Common;
using SupplyGuard.Domain.Enums;

namespace SupplyGuard.Domain.Entities;

public class XAIAuditLog : BaseEntity
{
    public Guid SupplierId { get; private set; }
    public Guid? RiskAssessmentId { get; private set; }
    public string ModelName { get; private set; } = null!;
    public string ModelVersion { get; private set; } = null!;
    public string CorrelationId { get; private set; } = null!;
    public ExplanationStatus ExplanationStatus { get; private set; }
    public string RequestPayload { get; private set; } = null!;
    public string? ResponsePayload { get; private set; }
    public string? ExplanationText { get; private set; }
    public Guid? RequestMessageId { get; private set; }
    public DateTimeOffset? RequestedAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
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
        string? failureMessage = null,
        ExplanationStatus? explanationStatus = null)
        : base(Guid.NewGuid())
    {
        SupplierId = RequireId(supplierId, nameof(supplierId));
        RiskAssessmentId = riskAssessmentId is null ? null : RequireId(riskAssessmentId.Value, nameof(riskAssessmentId));
        ModelName = RequireText(modelName, nameof(modelName), 200);
        ModelVersion = RequireText(modelVersion, nameof(modelVersion), 100);
        CorrelationId = RequireText(correlationId, nameof(correlationId), 128);
        ExplanationStatus = explanationStatus ?? (isSuccessful ? ExplanationStatus.Completed : ExplanationStatus.Failed);
        RequestPayload = RequireText(requestPayload, nameof(requestPayload), 100_000);
        ResponsePayload = NormalizeOptionalText(responsePayload, 100_000);
        ConfidenceScore = RequireConfidenceScore(confidenceScore);
        LatencyMs = latencyMs >= 0 ? latencyMs : throw new ArgumentOutOfRangeException(nameof(latencyMs), "Latency cannot be negative.");
        IsSuccessful = isSuccessful;
        FailureCode = NormalizeOptionalText(failureCode, 100);
        FailureMessage = NormalizeOptionalText(failureMessage, 2_000);
        ExecutedAtUtc = executedAtUtc.ToUniversalTime();

        if (ExplanationStatus == ExplanationStatus.Completed && (!isSuccessful || ResponsePayload is null))
        {
            throw new ArgumentException("A completed explanation requires a successful response payload.", nameof(responsePayload));
        }

        if (ExplanationStatus == ExplanationStatus.Failed && (isSuccessful || FailureCode is null))
        {
            throw new ArgumentException("A failed explanation requires a failure code and cannot be successful.", nameof(failureCode));
        }

        if (ExplanationStatus == ExplanationStatus.Pending && (isSuccessful || ResponsePayload is not null || FailureCode is not null))
        {
            throw new ArgumentException("A pending explanation cannot contain a response or failure outcome.", nameof(explanationStatus));
        }

        if (ExplanationStatus == ExplanationStatus.RuleBased && ResponsePayload is null)
        {
            throw new ArgumentException("A rule-based explanation requires an explanation payload.", nameof(responsePayload));
        }
    }

    public static XAIAuditLog CreatePending(Guid supplierId, string correlationId, Guid requestMessageId,
        string requestPayload, DateTimeOffset requestedAtUtc) => new(supplierId,
            "SupplyGuard_JIT_Model", "unreported", correlationId, requestPayload, null, 0, 0, false,
            requestedAtUtc, explanationStatus: ExplanationStatus.Pending)
        {
            RequestMessageId = RequireId(requestMessageId, nameof(requestMessageId)),
            RequestedAtUtc = requestedAtUtc.ToUniversalTime()
        };

    public void Complete(decimal confidenceScore, string explanationText, string responsePayload, DateTimeOffset completedAtUtc)
    {
        EnsurePending(completedAtUtc);
        // Validate every value before mutating the aggregate.
        var score = RequireConfidenceScore(confidenceScore);
        var explanation = RequireText(explanationText, nameof(explanationText), 100_000);
        var payload = RequireText(responsePayload, nameof(responsePayload), 100_000);
        ConfidenceScore = score;
        ExplanationText = explanation;
        ResponsePayload = payload;
        IsSuccessful = true;
        FailureCode = null;
        FailureMessage = null;
        CompletedAtUtc = completedAtUtc.ToUniversalTime();
        ExplanationStatus = ExplanationStatus.Completed;
    }

    public void Fail(string errorMessage, string responsePayload, DateTimeOffset completedAtUtc)
    {
        EnsurePending(completedAtUtc);
        var error = RequireText(errorMessage, nameof(errorMessage), 2_000);
        var payload = RequireText(responsePayload, nameof(responsePayload), 100_000);
        ConfidenceScore = 0;
        ExplanationText = string.Empty;
        ResponsePayload = payload;
        IsSuccessful = false;
        FailureCode = "ML_ANALYSIS_FAILED";
        FailureMessage = error;
        CompletedAtUtc = completedAtUtc.ToUniversalTime();
        ExplanationStatus = ExplanationStatus.Failed;
    }

    private void EnsurePending(DateTimeOffset completedAtUtc)
    {
        if (ExplanationStatus != ExplanationStatus.Pending)
            throw new InvalidOperationException("Only a pending audit can receive a result.");
        if (completedAtUtc < (RequestedAtUtc ?? ExecutedAtUtc))
            throw new ArgumentOutOfRangeException(nameof(completedAtUtc), "Completion cannot predate the request.");
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
