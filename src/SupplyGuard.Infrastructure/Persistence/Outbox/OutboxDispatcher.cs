using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SupplyGuard.Application.Common.Interfaces;
using SupplyGuard.Application.Common.Messaging;

namespace SupplyGuard.Infrastructure.Persistence.Outbox;

/// <summary>
/// Processes one bounded batch. Run one dispatcher instance at a time until database row claiming is added.
/// Delivery is at least once: broker confirmation and database completion are separate operations.
/// </summary>
public sealed class OutboxDispatcher(
    SupplyGuardDbContext dbContext,
    IMessagePublisher publisher,
    IOptions<OutboxProcessorOptions> options,
    TimeProvider timeProvider,
    ILogger<OutboxDispatcher> logger)
{
    public async Task DispatchBatchAsync(CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        var now = timeProvider.GetUtcNow();
        var pendingMessages = await dbContext.OutboxMessages
            .Where(message => message.ProcessedAtUtc == null && message.NextAttemptAtUtc <= now)
            .OrderBy(message => message.NextAttemptAtUtc)
            .ThenBy(message => message.OccurredAtUtc)
            .ThenBy(message => message.Id)
            .Take(settings.BatchSize)
            .ToListAsync(cancellationToken);

        foreach (var outboxMessage in pendingMessages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var message = Deserialize(outboxMessage);
                using var timeout = new CancellationTokenSource(settings.PublishTimeout, timeProvider);
                using var publishCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken, timeout.Token);

                // The publisher contract completes only after successful broker confirmation.
                await publisher.PublishAsync(message, publishCancellation.Token);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                var delayMilliseconds = Math.Min(
                    settings.MaxRetryDelay.TotalMilliseconds,
                    settings.RetryDelay.TotalMilliseconds * Math.Pow(2, Math.Min(outboxMessage.RetryCount, 20)));
                outboxMessage.RecordFailure(
                    $"Dispatch failed: {exception.GetType().Name}",
                    timeProvider.GetUtcNow().AddMilliseconds(delayMilliseconds));

                // Exception messages/payloads may contain sensitive data; log only identifiers and type.
                logger.LogWarning(
                    "Outbox message {MessageId} failed with {FailureType}; attempt {RetryCount}, retry at {NextAttemptAtUtc}",
                    outboxMessage.Id, exception.GetType().Name, outboxMessage.RetryCount, outboxMessage.NextAttemptAtUtc);

                await dbContext.SaveChangesAsync(cancellationToken);
                continue;
            }

            outboxMessage.MarkAsProcessed(timeProvider.GetUtcNow());

            // Save outside the publish catch. A save failure must abort this batch and dispose the scope.
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Outbox message {MessageId} publication confirmed", outboxMessage.Id);
        }
    }

    private static IIntegrationMessage Deserialize(OutboxMessage outboxMessage)
    {
        // An explicit allowlist keeps stored type names independent of CLR names and reflection.
        IIntegrationMessage message = outboxMessage.MessageType switch
        {
            EarlyWarningRaisedV1.TypeName => JsonSerializer.Deserialize<EarlyWarningRaisedV1>(outboxMessage.Payload)
                ?? throw new JsonException("The integration message payload is null."),
            _ => throw new NotSupportedException("The integration message type is not supported.")
        };

        if (message.MessageId != outboxMessage.Id ||
            message.MessageType != outboxMessage.MessageType ||
            message.CorrelationId != outboxMessage.CorrelationId ||
            message.OccurredAtUtc != outboxMessage.OccurredAtUtc)
        {
            throw new InvalidOperationException("The integration message metadata does not match the outbox record.");
        }

        return message;
    }
}
