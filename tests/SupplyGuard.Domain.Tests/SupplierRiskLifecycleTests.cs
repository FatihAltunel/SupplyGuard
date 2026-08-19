using SupplyGuard.Domain.Entities;
using SupplyGuard.Domain.Enums;
using SupplyGuard.Domain.Events;
using SupplyGuard.Domain.Services;

namespace SupplyGuard.Domain.Tests;

public class SupplierRiskLifecycleTests
{
    [Fact]
    public void ApplyAcknowledgeAndResolveRiskWarning_MutatesStateAndRaisesEvents()
    {
        var assessedAtUtc = new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);
        var supplier = new Supplier("Risky Supplier", "TR-321", "TR");
        var indicator = new RiskIndicator(
            supplier.Id,
            RiskCategory.Financial,
            "FIN-CRITICAL",
            "Critical financial exposure",
            RiskLevel.Critical,
            90m,
            90m,
            1m,
            "DomainTest",
            assessedAtUtc);
        var result = new RiskAssessmentService().Evaluate(supplier, new[] { indicator }, assessedAtUtc);

        supplier.ApplyRiskAssessment(result, "risk-evaluation-123");

        var warning = Assert.Single(supplier.EarlyWarnings);
        Assert.Single(supplier.RiskAssessments);
        Assert.Collection(
            supplier.DomainEvents,
            domainEvent => Assert.IsType<SupplierRiskAssessedEvent>(domainEvent),
            domainEvent => Assert.IsType<EarlyWarningTriggeredEvent>(domainEvent));

        var userId = Guid.NewGuid();
        supplier.AcknowledgeEarlyWarning(warning.Id, userId, assessedAtUtc.AddMinutes(5));
        supplier.ResolveEarlyWarning(warning.Id, userId, "Mitigation plan accepted.", assessedAtUtc.AddMinutes(10));

        Assert.Equal(WarningStatus.Resolved, warning.Status);
        Assert.IsType<EarlyWarningAcknowledgedEvent>(supplier.DomainEvents.ElementAt(2));
        Assert.IsType<EarlyWarningResolvedEvent>(supplier.DomainEvents.ElementAt(3));
    }
}
