using SupplyGuard.Domain.Entities;
using SupplyGuard.Domain.Enums;
using SupplyGuard.Domain.Services;

namespace SupplyGuard.Domain.Tests;

public sealed class ModelRiskAssessmentTests
{
    private static readonly DateTimeOffset AssessedAt = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0, RiskLevel.Low)]
    [InlineData(24.99, RiskLevel.Low)]
    [InlineData(25, RiskLevel.Medium)]
    [InlineData(49.99, RiskLevel.Medium)]
    [InlineData(50, RiskLevel.High)]
    [InlineData(74.99, RiskLevel.High)]
    [InlineData(75, RiskLevel.Critical)]
    [InlineData(100, RiskLevel.Critical)]
    public void ModelAssessment_DerivesLevelFromDomainPolicy(double score, RiskLevel expected)
    {
        var assessment = RiskAssessment.FromModelScore(Guid.NewGuid(), (decimal)score, AssessedAt, "Dummy rationale.");
        Assert.Equal(expected, assessment.OverallRiskLevel);
        Assert.Equal(expected, RiskLevelPolicy.FromScore((decimal)score));
        Assert.Equal((decimal)score, assessment.OverallRiskScore);
        Assert.Empty(assessment.RiskScores);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(100.01)]
    [InlineData(65.001)]
    public void ModelAssessment_RejectsInvalidScores(double score) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RiskAssessment.FromModelScore(Guid.NewGuid(), (decimal)score, AssessedAt, "Dummy rationale."));

    [Fact]
    public void ModelAssessment_RequiresExplanation() =>
        Assert.Throws<ArgumentException>(() => RiskAssessment.FromModelScore(Guid.NewGuid(), 65m, AssessedAt, " "));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OutOfOrderAssessments_DoNotRegressLatestDate(bool useRuleBasedPath)
    {
        var supplier = new Supplier("Supplier", "TEST-001", "TR");
        var newest = RiskAssessment.FromModelScore(supplier.Id, 65m, AssessedAt, "Newer result.");
        var older = RiskAssessment.FromModelScore(supplier.Id, 30m, AssessedAt.AddMinutes(-5), "Older result.");
        supplier.AddRiskAssessment(newest);
        if (useRuleBasedPath)
        {
            supplier.ApplyRiskAssessment(new RiskAssessmentResult(older, null), "legacy-correlation");
        }
        else
        {
            supplier.AddRiskAssessment(older);
        }

        Assert.Equal(AssessedAt, supplier.LastRiskAssessmentAtUtc);
        Assert.Equal(2, supplier.RiskAssessments.Count);
    }

    [Fact]
    public void AddRiskAssessment_RejectsDuplicatesAndForeignSuppliers()
    {
        var supplier = new Supplier("Supplier", "TEST-002", "TR");
        var assessment = RiskAssessment.FromModelScore(supplier.Id, 65m, AssessedAt, "Dummy rationale.");
        supplier.AddRiskAssessment(assessment);
        Assert.Throws<InvalidOperationException>(() => supplier.AddRiskAssessment(assessment));
        Assert.Throws<InvalidOperationException>(() => supplier.AddRiskAssessment(
            RiskAssessment.FromModelScore(Guid.NewGuid(), 65m, AssessedAt, "Dummy rationale.")));
        Assert.Single(supplier.RiskAssessments);
        Assert.Empty(supplier.DomainEvents);
    }
}
