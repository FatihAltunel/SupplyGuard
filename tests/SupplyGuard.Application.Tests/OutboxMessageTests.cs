using System.Text.Json;
using SupplyGuard.Application.Common.Messaging;

namespace SupplyGuard.Application.Tests;

public class OutboxMessageTests
{
    [Fact]
    public void RetryThenConfirmation_PreservesMessageIdentityAndPayload()
    {
        var message = CreateMessage();
        var payload = JsonSerializer.Serialize(message);
        var outbox = new OutboxMessage(
            message.MessageId, message.MessageType, payload, message.CorrelationId, message.OccurredAtUtc);

        outbox.RecordFailure("Broker unavailable", message.OccurredAtUtc.AddMinutes(1));
        outbox.RecordFailure("Confirmation timed out", message.OccurredAtUtc.AddMinutes(2));

        Assert.Null(outbox.ProcessedAtUtc);
        Assert.Equal(2, outbox.RetryCount);
        Assert.Equal(message.OccurredAtUtc.AddMinutes(2), outbox.NextAttemptAtUtc);

        outbox.MarkAsProcessed(message.OccurredAtUtc.AddMinutes(3));

        Assert.Equal(message.MessageId, outbox.Id);
        Assert.Equal(payload, outbox.Payload);
        Assert.Equal(message.OccurredAtUtc.AddMinutes(3), outbox.ProcessedAtUtc);
        Assert.Null(outbox.LastError);
        Assert.Throws<InvalidOperationException>(() =>
            outbox.RecordFailure("Must not retry", message.OccurredAtUtc.AddMinutes(4)));
    }

    [Fact]
    public void DeliveryTimestamps_AreNormalizedToUtc()
    {
        var occurrence = new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.FromHours(3));
        var outbox = new OutboxMessage(Guid.NewGuid(), EarlyWarningRaisedV1.TypeName, "{}", "trace-001", occurrence);

        outbox.RecordFailure("Broker unavailable", occurrence.AddMinutes(1));
        outbox.MarkAsProcessed(occurrence.AddMinutes(2));

        Assert.Equal(TimeSpan.Zero, outbox.OccurredAtUtc.Offset);
        Assert.Equal(TimeSpan.Zero, outbox.NextAttemptAtUtc.Offset);
        Assert.Equal(TimeSpan.Zero, outbox.ProcessedAtUtc!.Value.Offset);
        Assert.Equal(occurrence.UtcDateTime, outbox.OccurredAtUtc.UtcDateTime);
    }

    [Fact]
    public void EarlyWarningContract_RoundTripsWithStableVersionedType()
    {
        var message = CreateMessage();

        var restored = JsonSerializer.Deserialize<EarlyWarningRaisedV1>(JsonSerializer.Serialize(message));

        Assert.Equal(message, restored);
        Assert.Equal("early-warning.raised.v1", restored!.MessageType);
    }

    private static EarlyWarningRaisedV1 CreateMessage() => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, "Critical", "trace-001",
        new DateTimeOffset(2026, 10, 16, 9, 0, 0, TimeSpan.Zero));
}
