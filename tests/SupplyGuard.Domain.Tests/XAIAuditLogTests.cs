using SupplyGuard.Domain.Entities;
using SupplyGuard.Domain.Enums;

namespace SupplyGuard.Domain.Tests;

public class XAIAuditLogTests
{
    [Fact]
    public void Constructor_AllowsPendingExplanationWithoutExternalResponse()
    {
        var log = new XAIAuditLog(
            Guid.NewGuid(),
            "Unassigned",
            "Unassigned",
            "risk-evaluation-123",
            "{\"assessment\":\"snapshot\"}",
            null,
            0m,
            0,
            false,
            DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            explanationStatus: ExplanationStatus.Pending);

        Assert.Equal(ExplanationStatus.Pending, log.ExplanationStatus);
        Assert.Equal("risk-evaluation-123", log.CorrelationId);
        Assert.Null(log.ResponsePayload);
    }
}
