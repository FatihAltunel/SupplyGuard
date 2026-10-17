namespace SupplyGuard.Application.Common.Messaging;

/// <summary>
/// Delivery state staged in the same transaction as the business changes.
/// This record has no dependency on EF Core or a message broker.
/// </summary>
public sealed class OutboxMessage
{
    public const int MessageTypeMaxLength = 200;
    public const int CorrelationIdMaxLength = 128;
    public const int LastErrorMaxLength = 2_000;

    // The integration message identifier, reused across every publication attempt.
    public Guid Id { get; private set; }
    public string MessageType { get; private set; } = null!;
    public string Payload { get; private set; } = null!;
    public string CorrelationId { get; private set; } = null!;
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public DateTimeOffset NextAttemptAtUtc { get; private set; }
    public DateTimeOffset? ProcessedAtUtc { get; private set; }
    public int RetryCount { get; private set; }
    public string? LastError { get; private set; }

    private OutboxMessage()
    {
        // Required by persistence materialization.
    }

    public OutboxMessage(
        Guid id,
        string messageType,
        string payload,
        string correlationId,
        DateTimeOffset occurredAtUtc)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Message identifier cannot be empty.", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(payload);

        Id = id;
        MessageType = RequireText(messageType, nameof(messageType), MessageTypeMaxLength);
        Payload = payload;
        CorrelationId = RequireText(correlationId, nameof(correlationId), CorrelationIdMaxLength);
        OccurredAtUtc = occurredAtUtc.ToUniversalTime();
        NextAttemptAtUtc = OccurredAtUtc;
    }

    /// <summary>Call only after the broker confirms a successfully routed publication.</summary>
    public void MarkAsProcessed(DateTimeOffset processedAtUtc)
    {
        EnsurePending();
        var processedTime = processedAtUtc.ToUniversalTime();
        if (processedTime < OccurredAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(processedAtUtc), "Processing cannot predate occurrence.");
        }

        ProcessedAtUtc = processedTime;
        LastError = null;
    }

    /// <summary>The caller must supply a sanitized diagnostic without secrets or payload data.</summary>
    public void RecordFailure(string error, DateTimeOffset nextAttemptAtUtc)
    {
        EnsurePending();
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        var nextAttemptTime = nextAttemptAtUtc.ToUniversalTime();
        if (nextAttemptTime < OccurredAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(nextAttemptAtUtc), "Retry cannot predate occurrence.");
        }

        RetryCount = checked(RetryCount + 1);
        LastError = error.Length > LastErrorMaxLength ? error[..LastErrorMaxLength] : error;
        NextAttemptAtUtc = nextAttemptTime;
    }

    private void EnsurePending()
    {
        if (ProcessedAtUtc is not null)
        {
            throw new InvalidOperationException("The message has already been processed.");
        }
    }

    private static string RequireText(string value, string parameterName, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength)
        {
            throw new ArgumentException($"Value must contain between 1 and {maximumLength} characters.", parameterName);
        }

        return value;
    }
}
