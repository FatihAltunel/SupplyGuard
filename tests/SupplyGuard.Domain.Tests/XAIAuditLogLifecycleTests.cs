using SupplyGuard.Domain.Entities;
using SupplyGuard.Domain.Enums;

namespace SupplyGuard.Domain.Tests;

public sealed class XAIAuditLogLifecycleTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PendingAudit_AcceptsExactlyOneTerminalOutcome(bool success)
    {
        var audit = XAIAuditLog.CreatePending(Guid.NewGuid(), "request-123", Guid.NewGuid(), "{}", Now);
        if (success) audit.Complete(.996m, "Risk açıklaması", "{}", Now.AddSeconds(2));
        else audit.Fail("Model unavailable", "{}", Now.AddSeconds(2));
        Assert.Equal(success ? ExplanationStatus.Completed : ExplanationStatus.Failed, audit.ExplanationStatus);
        Assert.Equal(success, audit.IsSuccessful);
        Assert.Equal(Now.AddSeconds(2), audit.CompletedAtUtc);
        Assert.Equal(Now, audit.RequestedAtUtc);
        Assert.Equal(success ? .996m : 0, audit.ConfidenceScore);
        Assert.Equal(success ? null : "ML_ANALYSIS_FAILED", audit.FailureCode);
        Assert.Throws<InvalidOperationException>(() => audit.Complete(.5m, "Overwrite", "{}", Now.AddSeconds(3)));
        Assert.Throws<InvalidOperationException>(() => audit.Fail("Overwrite", "{}", Now.AddSeconds(3)));
    }

    [Fact]
    public void InvalidOutcome_DoesNotPartiallyMutatePendingAudit()
    {
        var audit = XAIAuditLog.CreatePending(Guid.NewGuid(), "request-123", Guid.NewGuid(), "{}", Now);
        Assert.Throws<ArgumentException>(() => audit.Complete(.9m, "Valid", "", Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => audit.Complete(2, "Valid", "{}", Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => audit.Fail("Error", "{}", Now.AddSeconds(-1)));
        Assert.Equal(ExplanationStatus.Pending, audit.ExplanationStatus);
        Assert.Null(audit.ResponsePayload);
        Assert.Null(audit.ExplanationText);
        Assert.Null(audit.CompletedAtUtc);
        Assert.False(audit.IsSuccessful);
    }
}
