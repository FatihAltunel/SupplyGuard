using SupplyGuard.Domain.Common;
using SupplyGuard.Domain.Enums;

namespace SupplyGuard.Domain.Entities;

public class EarlyWarning : AuditableEntity
{
    public Guid SupplierId { get; private set; }
    public Guid? RiskAssessmentId { get; private set; }
    public string Title { get; private set; } = null!;
    public string Message { get; private set; } = null!;
    public WarningSeverity Severity { get; private set; }
    public WarningStatus Status { get; private set; }
    public DateTimeOffset DetectedAtUtc { get; private set; }
    public DateTimeOffset? AcknowledgedAtUtc { get; private set; }
    public Guid? AcknowledgedByUserId { get; private set; }
    public DateTimeOffset? ResolvedAtUtc { get; private set; }
    public Guid? ResolvedByUserId { get; private set; }
    public string? ResolutionNote { get; private set; }
    public Supplier Supplier { get; private set; } = null!;
    public RiskAssessment? RiskAssessment { get; private set; }

    private EarlyWarning()
    {
        // Required by EF Core.
    }

    public EarlyWarning(Guid supplierId, string title, string message, WarningSeverity severity, DateTimeOffset detectedAtUtc, Guid? riskAssessmentId = null, Guid? createdByUserId = null)
        : base(createdByUserId)
    {
        SupplierId = RequireId(supplierId, nameof(supplierId));
        RiskAssessmentId = riskAssessmentId is null ? null : RequireId(riskAssessmentId.Value, nameof(riskAssessmentId));
        Title = RequireText(title, nameof(title), 200);
        Message = RequireText(message, nameof(message), 4_000);
        Severity = severity;
        Status = WarningStatus.Open;
        DetectedAtUtc = detectedAtUtc.ToUniversalTime();
    }

    public void Acknowledge(Guid acknowledgedByUserId, DateTimeOffset acknowledgedAtUtc)
    {
        if (Status is WarningStatus.Resolved or WarningStatus.Dismissed)
        {
            throw new InvalidOperationException("A closed warning cannot be acknowledged.");
        }

        if (Status == WarningStatus.Acknowledged)
        {
            throw new InvalidOperationException("The warning has already been acknowledged.");
        }

        var acknowledgementTime = acknowledgedAtUtc.ToUniversalTime();
        if (acknowledgementTime < DetectedAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(acknowledgedAtUtc), "Acknowledgement cannot predate warning detection.");
        }

        AcknowledgedByUserId = RequireId(acknowledgedByUserId, nameof(acknowledgedByUserId));
        AcknowledgedAtUtc = acknowledgementTime;
        Status = WarningStatus.Acknowledged;
        MarkAsModified(acknowledgedByUserId);
    }

    public void Resolve(Guid resolvedByUserId, string resolutionNote, DateTimeOffset resolvedAtUtc)
    {
        if (Status is WarningStatus.Resolved or WarningStatus.Dismissed)
        {
            throw new InvalidOperationException("The warning is already closed.");
        }

        if (Status != WarningStatus.Acknowledged)
        {
            throw new InvalidOperationException("The warning must be acknowledged before it can be resolved.");
        }

        var resolutionTime = resolvedAtUtc.ToUniversalTime();
        if (resolutionTime < AcknowledgedAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(resolvedAtUtc), "Resolution cannot predate acknowledgement.");
        }

        ResolvedByUserId = RequireId(resolvedByUserId, nameof(resolvedByUserId));
        ResolutionNote = RequireText(resolutionNote, nameof(resolutionNote), 2_000);
        ResolvedAtUtc = resolutionTime;
        Status = WarningStatus.Resolved;
        MarkAsModified(resolvedByUserId);
    }

    public void Dismiss(Guid dismissedByUserId, string reason, DateTimeOffset dismissedAtUtc)
    {
        if (Status is WarningStatus.Resolved or WarningStatus.Dismissed)
        {
            throw new InvalidOperationException("The warning is already closed.");
        }

        ResolutionNote = RequireText(reason, nameof(reason), 2_000);
        ResolvedByUserId = RequireId(dismissedByUserId, nameof(dismissedByUserId));
        ResolvedAtUtc = dismissedAtUtc.ToUniversalTime();
        Status = WarningStatus.Dismissed;
        MarkAsModified(dismissedByUserId);
    }

    private static Guid RequireId(Guid value, string parameterName) => value == Guid.Empty ? throw new ArgumentException("Identifier cannot be empty.", parameterName) : value;
    private static string RequireText(string value, string parameterName, int maximumLength)
    {
        var normalizedValue = value?.Trim();
        return string.IsNullOrWhiteSpace(normalizedValue) || normalizedValue.Length > maximumLength
            ? throw new ArgumentException($"Value must contain between 1 and {maximumLength} characters.", parameterName)
            : normalizedValue;
    }
}
