using SupplyGuard.Domain.Entities;
using SupplyGuard.Domain.Enums;
using SupplyGuard.Domain.Services;

namespace SupplyGuard.Domain.Tests;

public class RiskAssessmentServiceTests
{
    private readonly RiskAssessmentService _service = new();

    [Fact]
    public void Evaluate_CalculatesWeightedScoresAndTriggersWarningForCriticalSupplier()
    {
        var assessedAtUtc = new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);
        var supplier = new Supplier("Critical Parts Ltd", "TR-123", "TR");
        supplier.SetCriticality(true);
        var indicators = new[]
        {
            CreateIndicator(supplier.Id, RiskCategory.Financial, "FIN-01", 80m, 0.6m, assessedAtUtc),
            CreateIndicator(supplier.Id, RiskCategory.Operational, "OPS-01", 60m, 0.4m, assessedAtUtc)
        };

        var result = _service.Evaluate(supplier, indicators, assessedAtUtc);

        Assert.Equal(72m, result.Assessment.OverallRiskScore);
        Assert.Equal(RiskLevel.High, result.Assessment.OverallRiskLevel);
        Assert.Equal(2, result.Assessment.RiskScores.Count);
        Assert.NotNull(result.EarlyWarning);
        Assert.Equal(WarningSeverity.High, result.EarlyWarning!.Severity);
        Assert.Equal(result.Assessment.Id, result.EarlyWarning.RiskAssessmentId);
    }

    [Fact]
    public void Evaluate_DoesNotTriggerWarningBelowStandardThreshold()
    {
        var assessedAtUtc = new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);
        var supplier = new Supplier("Standard Parts Ltd", "TR-456", "TR");
        var indicators = new[]
        {
            CreateIndicator(supplier.Id, RiskCategory.Quality, "QUA-01", 74m, 1m, assessedAtUtc)
        };

        var result = _service.Evaluate(supplier, indicators, assessedAtUtc);

        Assert.Equal(74m, result.Assessment.OverallRiskScore);
        Assert.Null(result.EarlyWarning);
    }

    [Fact]
    public void Evaluate_RejectsIndicatorsOwnedByAnotherSupplier()
    {
        var assessedAtUtc = new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);
        var supplier = new Supplier("Supplier A", "TR-789", "TR");
        var otherSupplier = new Supplier("Supplier B", "TR-987", "TR");
        var indicators = new[]
        {
            CreateIndicator(otherSupplier.Id, RiskCategory.Compliance, "COM-01", 80m, 1m, assessedAtUtc)
        };

        Assert.Throws<InvalidOperationException>(() =>
            _service.Evaluate(supplier, indicators, assessedAtUtc));
    }

    private static RiskIndicator CreateIndicator(
        Guid supplierId,
        RiskCategory category,
        string code,
        decimal score,
        decimal weight,
        DateTimeOffset observedAtUtc) =>
        new(
            supplierId,
            category,
            code,
            code,
            RiskLevel.High,
            score,
            score,
            weight,
            "DomainTest",
            observedAtUtc);
}
