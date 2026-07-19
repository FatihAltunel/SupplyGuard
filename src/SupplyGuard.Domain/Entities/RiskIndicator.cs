using SupplyGuard.Domain.Common;
using SupplyGuard.Domain.Enums;

namespace SupplyGuard.Domain.Entities;

public class RiskIndicator : AuditableEntity
{
    public Guid SupplierId { get; private set; }
    public RiskCategory Category { get; private set; }
    public string IndicatorCode { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public RiskLevel Severity { get; private set; }
    public decimal RawValue { get; private set; }
    public string? Unit { get; private set; }
    public decimal NormalizedScore { get; private set; }
    public decimal Weight { get; private set; }
    public string SourceSystem { get; private set; } = null!;
    public string? SourceReference { get; private set; }
    public DateTimeOffset ObservedAtUtc { get; private set; }
    public DateTimeOffset? ExpiresAtUtc { get; private set; }
    public bool IsActive { get; private set; }
    public Supplier Supplier { get; private set; } = null!;

    private RiskIndicator()
    {
        // Required by EF Core.
    }

    public RiskIndicator(
        Guid supplierId,
        RiskCategory category,
        string indicatorCode,
        string name,
        RiskLevel severity,
        decimal rawValue,
        decimal normalizedScore,
        decimal weight,
        string sourceSystem,
        DateTimeOffset observedAtUtc,
        Guid? createdByUserId = null)
        : base(createdByUserId)
    {
        SupplierId = RequireId(supplierId, nameof(supplierId));
        Category = category;
        IndicatorCode = RequireText(indicatorCode, nameof(indicatorCode), 100).ToUpperInvariant();
        Name = RequireText(name, nameof(name), 200);
        Severity = severity;
        RawValue = rawValue;
        NormalizedScore = RequireScore(normalizedScore, nameof(normalizedScore));
        Weight = RequireWeight(weight);
        SourceSystem = RequireText(sourceSystem, nameof(sourceSystem), 100);
        ObservedAtUtc = observedAtUtc.ToUniversalTime();
        IsActive = true;
    }

    public void UpdateMeasurement(
        decimal rawValue,
        decimal normalizedScore,
        RiskLevel severity,
        DateTimeOffset observedAtUtc,
        Guid? modifiedByUserId = null)
    {
        RawValue = rawValue;
        NormalizedScore = RequireScore(normalizedScore, nameof(normalizedScore));
        Severity = severity;
        ObservedAtUtc = observedAtUtc.ToUniversalTime();
        MarkAsModified(modifiedByUserId);
    }

    public void UpdateSourceDetails(string? description, string? unit, string? sourceReference, DateTimeOffset? expiresAtUtc, Guid? modifiedByUserId = null)
    {
        Description = NormalizeOptionalText(description, 1_000);
        Unit = NormalizeOptionalText(unit, 32);
        SourceReference = NormalizeOptionalText(sourceReference, 500);
        ExpiresAtUtc = expiresAtUtc?.ToUniversalTime();

        if (ExpiresAtUtc is not null && ExpiresAtUtc <= ObservedAtUtc)
        {
            throw new ArgumentException("The expiration time must be later than the observation time.", nameof(expiresAtUtc));
        }

        MarkAsModified(modifiedByUserId);
    }

    public void Deactivate(Guid? modifiedByUserId = null)
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        MarkAsModified(modifiedByUserId);
    }

    private static Guid RequireId(Guid value, string parameterName) =>
        value == Guid.Empty ? throw new ArgumentException("Identifier cannot be empty.", parameterName) : value;

    private static decimal RequireScore(decimal value, string parameterName)
    {
        if (value is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Score must be between 0 and 100.");
        }

        return value;
    }

    private static decimal RequireWeight(decimal value)
    {
        if (value is <= 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Weight must be greater than 0 and no greater than 1.");
        }

        return value;
    }

    private static string RequireText(string value, string parameterName, int maximumLength)
    {
        var normalizedValue = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedValue) || normalizedValue.Length > maximumLength)
        {
            throw new ArgumentException($"Value must contain between 1 and {maximumLength} characters.", parameterName);
        }

        return normalizedValue;
    }

    private static string? NormalizeOptionalText(string? value, int maximumLength) =>
        string.IsNullOrWhiteSpace(value) ? null : RequireText(value, nameof(value), maximumLength);
}
