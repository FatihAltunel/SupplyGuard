using SupplyGuard.Domain.Entities;
using SupplyGuard.Domain.Enums;

namespace SupplyGuard.Domain.Services;

public sealed class RiskAssessmentService : IRiskAssessmentService
{
    public const decimal StandardWarningThreshold = 75m;
    public const decimal CriticalSupplierWarningThreshold = 50m;

    public RiskAssessmentResult Evaluate(
        Supplier supplier,
        IReadOnlyCollection<RiskIndicator> indicators,
        DateTimeOffset assessedAtUtc,
        Guid? createdByUserId = null)
    {
        ArgumentNullException.ThrowIfNull(supplier);
        ArgumentNullException.ThrowIfNull(indicators);

        var assessmentTime = assessedAtUtc.ToUniversalTime();
        var applicableIndicators = indicators
            .Where(indicator => indicator.SupplierId == supplier.Id)
            .Where(indicator => indicator.IsActive)
            .Where(indicator => indicator.ObservedAtUtc <= assessmentTime)
            .Where(indicator => indicator.ExpiresAtUtc is null || indicator.ExpiresAtUtc > assessmentTime)
            .ToArray();

        if (indicators.Any(indicator => indicator.SupplierId != supplier.Id))
        {
            throw new InvalidOperationException("All risk indicators must belong to the assessed supplier.");
        }

        if (applicableIndicators.Length == 0)
        {
            throw new InvalidOperationException("At least one active, non-expired risk indicator is required.");
        }

        var categoryGroups = applicableIndicators
            .GroupBy(indicator => indicator.Category)
            .Select(group => new
            {
                Category = group.Key,
                Indicators = group.ToArray(),
                Weight = group.Sum(indicator => indicator.Weight)
            })
            .OrderBy(group => group.Category)
            .ToArray();

        var totalWeight = categoryGroups.Sum(group => group.Weight);
        var assessment = new RiskAssessment(
            supplier.Id,
            0m,
            RiskLevel.Low,
            assessmentTime,
            "Calculated from active, non-expired supplier risk indicators.",
            createdByUserId: createdByUserId);

        foreach (var group in categoryGroups)
        {
            var categoryScore = Math.Round(
                group.Indicators.Sum(indicator => indicator.NormalizedScore * indicator.Weight) / group.Weight,
                2,
                MidpointRounding.AwayFromZero);
            var normalizedCategoryWeight = group.Weight / totalWeight;
            var explanation = $"Calculated from {group.Indicators.Length} active {group.Category} indicator(s).";

            assessment.AddRiskScore(new RiskScore(
                assessment.Id,
                group.Category,
                categoryScore,
                normalizedCategoryWeight,
                ToRiskLevel(categoryScore),
                assessmentTime,
                explanation));
        }

        var warningThreshold = supplier.IsCriticalSupplier
            ? CriticalSupplierWarningThreshold
            : StandardWarningThreshold;

        if (assessment.OverallRiskScore < warningThreshold)
        {
            return new RiskAssessmentResult(assessment, null);
        }

        var warning = new EarlyWarning(
            supplier.Id,
            $"{assessment.OverallRiskLevel} supplier risk detected",
            $"The supplier risk score of {assessment.OverallRiskScore:F2} meets or exceeds the warning threshold of {warningThreshold:F2}.",
            ToWarningSeverity(assessment.OverallRiskLevel),
            assessmentTime,
            assessment.Id,
            createdByUserId);

        return new RiskAssessmentResult(assessment, warning);
    }

    private static RiskLevel ToRiskLevel(decimal score) => score switch
    {
        < 25m => RiskLevel.Low,
        < 50m => RiskLevel.Medium,
        < 75m => RiskLevel.High,
        _ => RiskLevel.Critical
    };

    private static WarningSeverity ToWarningSeverity(RiskLevel riskLevel) => riskLevel switch
    {
        RiskLevel.Low => WarningSeverity.Low,
        RiskLevel.Medium => WarningSeverity.Medium,
        RiskLevel.High => WarningSeverity.High,
        RiskLevel.Critical => WarningSeverity.Critical,
        _ => throw new ArgumentOutOfRangeException(nameof(riskLevel), riskLevel, "Unsupported risk level.")
    };
}
