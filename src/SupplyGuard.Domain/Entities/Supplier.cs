namespace SupplyGuard.Domain.Entities;

using SupplyGuard.Domain.Common;
using SupplyGuard.Domain.Enums;
using SupplyGuard.Domain.Events;
using SupplyGuard.Domain.Services;

public class Supplier : AuditableEntity
{
    private readonly List<RiskAssessment> _riskAssessments = [];
    private readonly List<RiskIndicator> _riskIndicators = [];
    private readonly List<EarlyWarning> _earlyWarnings = [];
    private readonly List<XAIAuditLog> _xaiAuditLogs = [];

    public string Name { get; private set; } = null!;
    public string TaxNumber { get; private set; } = null!;
    public string CountryCode { get; private set; } = null!;
    public string? RegistrationNumber { get; private set; }
    public SupplierStatus Status { get; private set; }
    public string? ContactName { get; private set; }
    public string? ContactEmail { get; private set; }
    public string? ContactPhone { get; private set; }
    public string? WebsiteUrl { get; private set; }
    public string? City { get; private set; }
    public string? Address { get; private set; }
    public string? Industry { get; private set; }
    public string? SupplierCategory { get; private set; }
    public bool IsCriticalSupplier { get; private set; }
    public DateTimeOffset? OnboardingDateUtc { get; private set; }
    public DateTimeOffset? LastRiskAssessmentAtUtc { get; private set; }

    public IReadOnlyCollection<RiskAssessment> RiskAssessments => _riskAssessments.AsReadOnly();
    public IReadOnlyCollection<RiskIndicator> RiskIndicators => _riskIndicators.AsReadOnly();
    public IReadOnlyCollection<EarlyWarning> EarlyWarnings => _earlyWarnings.AsReadOnly();
    public IReadOnlyCollection<XAIAuditLog> XAIAuditLogs => _xaiAuditLogs.AsReadOnly();

    private Supplier()
    {
        // Required by EF Core.
    }

    public Supplier(string name, string taxNumber, string countryCode, Guid? createdByUserId = null)
        : base(createdByUserId)
    {
        Name = RequireText(name, nameof(name), 200);
        TaxNumber = RequireText(taxNumber, nameof(taxNumber), 64).ToUpperInvariant();
        CountryCode = NormalizeCountryCode(countryCode);
        Status = SupplierStatus.Active;
    }

    public void ChangeStatus(SupplierStatus status, Guid? modifiedByUserId = null)
    {
        if (Status == status)
        {
            return;
        }

        Status = status;
        MarkAsModified(modifiedByUserId);
    }

    public void UpdateContactDetails(
        string? contactName,
        string? contactEmail,
        string? contactPhone,
        string? websiteUrl,
        Guid? modifiedByUserId = null)
    {
        ContactName = NormalizeOptionalText(contactName, 150);
        ContactEmail = NormalizeOptionalText(contactEmail, 320);
        ContactPhone = NormalizeOptionalText(contactPhone, 32);
        WebsiteUrl = NormalizeOptionalText(websiteUrl, 2048);
        MarkAsModified(modifiedByUserId);
    }

    public void UpdateProfile(
        string? registrationNumber,
        string? city,
        string? address,
        string? industry,
        string? supplierCategory,
        Guid? modifiedByUserId = null)
    {
        RegistrationNumber = NormalizeOptionalText(registrationNumber, 64);
        City = NormalizeOptionalText(city, 100);
        Address = NormalizeOptionalText(address, 500);
        Industry = NormalizeOptionalText(industry, 100);
        SupplierCategory = NormalizeOptionalText(supplierCategory, 100);
        MarkAsModified(modifiedByUserId);
    }

    public void SetCriticality(bool isCriticalSupplier, Guid? modifiedByUserId = null)
    {
        if (IsCriticalSupplier == isCriticalSupplier)
        {
            return;
        }

        IsCriticalSupplier = isCriticalSupplier;
        MarkAsModified(modifiedByUserId);
    }

    public void SetOnboardingDate(DateTimeOffset onboardingDateUtc, Guid? modifiedByUserId = null)
    {
        OnboardingDateUtc = onboardingDateUtc.ToUniversalTime();
        MarkAsModified(modifiedByUserId);
    }

    public void AddRiskAssessment(RiskAssessment riskAssessment, Guid? modifiedByUserId = null)
    {
        ArgumentNullException.ThrowIfNull(riskAssessment);
        EnsureBelongsToSupplier(riskAssessment.SupplierId);

        _riskAssessments.Add(riskAssessment);
        LastRiskAssessmentAtUtc = riskAssessment.AssessedAtUtc;
        MarkAsModified(modifiedByUserId);
    }

    public void ApplyRiskAssessment(
        RiskAssessmentResult result,
        string correlationId,
        Guid? modifiedByUserId = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        var normalizedCorrelationId = RequireText(correlationId, nameof(correlationId), 128);
        var assessment = result.Assessment;

        EnsureBelongsToSupplier(assessment.SupplierId);
        if (_riskAssessments.Any(existing => existing.Id == assessment.Id))
        {
            throw new InvalidOperationException("The risk assessment has already been applied.");
        }

        if (result.EarlyWarning is not null)
        {
            EnsureBelongsToSupplier(result.EarlyWarning.SupplierId);
            if (result.EarlyWarning.RiskAssessmentId != assessment.Id)
            {
                throw new InvalidOperationException("The early warning must reference the applied risk assessment.");
            }
        }

        _riskAssessments.Add(assessment);
        LastRiskAssessmentAtUtc = assessment.AssessedAtUtc;

        if (result.EarlyWarning is not null)
        {
            _earlyWarnings.Add(result.EarlyWarning);
        }

        MarkAsModified(modifiedByUserId);
        RaiseDomainEvent(new SupplierRiskAssessedEvent(
            Id,
            assessment.Id,
            assessment.OverallRiskScore,
            assessment.OverallRiskLevel,
            normalizedCorrelationId,
            assessment.AssessedAtUtc));

        if (result.EarlyWarning is not null)
        {
            RaiseDomainEvent(new EarlyWarningTriggeredEvent(
                Id,
                result.EarlyWarning.Id,
                assessment.Id,
                result.EarlyWarning.Severity,
                normalizedCorrelationId,
                result.EarlyWarning.DetectedAtUtc));
        }
    }

    public void AcknowledgeEarlyWarning(
        Guid earlyWarningId,
        Guid acknowledgedByUserId,
        DateTimeOffset acknowledgedAtUtc)
    {
        var warning = GetEarlyWarning(earlyWarningId);
        warning.Acknowledge(acknowledgedByUserId, acknowledgedAtUtc);
        MarkAsModified(acknowledgedByUserId);
        RaiseDomainEvent(new EarlyWarningAcknowledgedEvent(
            Id,
            warning.Id,
            acknowledgedByUserId,
            warning.AcknowledgedAtUtc!.Value));
    }

    public void ResolveEarlyWarning(
        Guid earlyWarningId,
        Guid resolvedByUserId,
        string resolutionNote,
        DateTimeOffset resolvedAtUtc)
    {
        var warning = GetEarlyWarning(earlyWarningId);
        warning.Resolve(resolvedByUserId, resolutionNote, resolvedAtUtc);
        MarkAsModified(resolvedByUserId);
        RaiseDomainEvent(new EarlyWarningResolvedEvent(
            Id,
            warning.Id,
            resolvedByUserId,
            warning.ResolutionNote!,
            warning.ResolvedAtUtc!.Value));
    }

    public void AddRiskIndicator(RiskIndicator riskIndicator, Guid? modifiedByUserId = null)
    {
        ArgumentNullException.ThrowIfNull(riskIndicator);
        EnsureBelongsToSupplier(riskIndicator.SupplierId);

        _riskIndicators.Add(riskIndicator);
        MarkAsModified(modifiedByUserId);
    }

    public void AddEarlyWarning(EarlyWarning earlyWarning, Guid? modifiedByUserId = null)
    {
        ArgumentNullException.ThrowIfNull(earlyWarning);
        EnsureBelongsToSupplier(earlyWarning.SupplierId);

        _earlyWarnings.Add(earlyWarning);
        MarkAsModified(modifiedByUserId);
    }

    public void AddXAIAuditLog(XAIAuditLog xaiAuditLog, Guid? modifiedByUserId = null)
    {
        ArgumentNullException.ThrowIfNull(xaiAuditLog);
        EnsureBelongsToSupplier(xaiAuditLog.SupplierId);

        _xaiAuditLogs.Add(xaiAuditLog);
        MarkAsModified(modifiedByUserId);
    }

    private void EnsureBelongsToSupplier(Guid supplierId)
    {
        if (supplierId != Id)
        {
            throw new InvalidOperationException("The related entity belongs to a different supplier.");
        }
    }

    private EarlyWarning GetEarlyWarning(Guid earlyWarningId)
    {
        if (earlyWarningId == Guid.Empty)
        {
            throw new ArgumentException("Identifier cannot be empty.", nameof(earlyWarningId));
        }

        return _earlyWarnings.SingleOrDefault(warning => warning.Id == earlyWarningId)
            ?? throw new InvalidOperationException("The early warning does not belong to this supplier.");
    }

    private static string NormalizeCountryCode(string countryCode)
    {
        var normalizedCountryCode = RequireText(countryCode, nameof(countryCode), 2).ToUpperInvariant();

        if (normalizedCountryCode.Length != 2 || !normalizedCountryCode.All(char.IsLetter))
        {
            throw new ArgumentException("Country code must be a two-letter ISO 3166-1 alpha-2 code.", nameof(countryCode));
        }

        return normalizedCountryCode;
    }

    private static string RequireText(string value, string parameterName, int maximumLength)
    {
        var normalizedValue = value?.Trim();

        if (string.IsNullOrWhiteSpace(normalizedValue))
        {
            throw new ArgumentException("Value is required.", parameterName);
        }

        if (normalizedValue.Length > maximumLength)
        {
            throw new ArgumentOutOfRangeException(parameterName, $"Value cannot exceed {maximumLength} characters.");
        }

        return normalizedValue;
    }

    private static string? NormalizeOptionalText(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return RequireText(value, nameof(value), maximumLength);
    }
}
