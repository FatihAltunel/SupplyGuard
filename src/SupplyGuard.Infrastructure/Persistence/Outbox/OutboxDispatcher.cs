using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using SupplyGuard.Application.Common.Interfaces;
using SupplyGuard.Application.Common.Messaging;
using SupplyGuard.Application.Common.Messaging.RiskAnalysis.V1;
using SupplyGuard.Application.Common.Messaging.RiskAnalysis.Jit.V1;
using SupplyGuard.Application.Common.Messaging.RiskAnalysis.Worker.V1;

namespace SupplyGuard.Infrastructure.Persistence.Outbox;

/// <summary>Each attempt owns a fresh scope, transaction, and PostgreSQL row claim.</summary>
public sealed class OutboxDispatcher(
    IServiceScopeFactory scopeFactory,
    IOptions<OutboxProcessorOptions> options,
    TimeProvider timeProvider,
    ILogger<OutboxDispatcher> logger) : IOutboxDispatcher
{
    public async Task DispatchBatchAsync(CancellationToken cancellationToken = default)
    {
        Guid[] ids;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<SupplyGuardDbContext>();
            var now = timeProvider.GetUtcNow();
            ids = await context.OutboxMessages.AsNoTracking()
                .Where(message => message.ProcessedAtUtc == null && message.QuarantinedAtUtc == null && message.NextAttemptAtUtc <= now)
                .OrderBy(message => message.NextAttemptAtUtc).ThenBy(message => message.OccurredAtUtc).ThenBy(message => message.Id)
                .Take(options.Value.BatchSize).Select(message => message.Id).ToArrayAsync(cancellationToken);
        }
        foreach (var id in ids) await TryDispatchAsync(id, cancellationToken);
    }

    public async Task<bool> TryDispatchAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SupplyGuardDbContext>();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        OutboxMessage? row;
        if (context.Database.IsNpgsql())
        {
            var rows = await context.OutboxMessages.FromSqlInterpolated($"""
                SELECT * FROM supplyguard.outbox_messages WHERE "Id" = {messageId}
                FOR UPDATE SKIP LOCKED
                """).ToListAsync(cancellationToken);
            row = rows.SingleOrDefault();
        }
        else if (context.Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
        {
            // SQLite is used by workflow tests; production claims require PostgreSQL.
            row = await context.OutboxMessages.SingleOrDefaultAsync(message => message.Id == messageId, cancellationToken);
        }
        else throw new NotSupportedException("Outbox dispatch requires PostgreSQL.");

        if (row is null) return false; // Missing or claimed by another dispatcher.
        if (row.ProcessedAtUtc is not null) return true;
        if (row.QuarantinedAtUtc is not null) return false;
        if (row.NextAttemptAtUtc > timeProvider.GetUtcNow()) return false;

        var serializer = scope.ServiceProvider.GetRequiredService<IIntegrationMessageSerializer>();
        var publisher = scope.ServiceProvider.GetRequiredService<IMessagePublisher>();
        IIntegrationMessage message;
        try
        {
            message = Deserialize(row, serializer);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or InvalidOperationException)
        {
            row.Quarantine($"Invalid retained contract: {exception.GetType().Name}", timeProvider.GetUtcNow());
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            logger.LogError("Outbox message {MessageId} quarantined: invalid or legacy contract", row.Id);
            return false;
        }
        try
        {
            using var timeout = new CancellationTokenSource(options.Value.PublishTimeout, timeProvider);
            using var publication = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            await publisher.PublishAsync(message, publication.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // Transaction disposal leaves the original row pending.
        }
        catch (Exception exception)
        {
            var settings = options.Value;
            var milliseconds = Math.Min(settings.MaxRetryDelay.TotalMilliseconds,
                settings.RetryDelay.TotalMilliseconds * Math.Pow(2, Math.Min(row.RetryCount, 20)));
            if (settings.RetryUseJitter) milliseconds *= 0.5 + Random.Shared.NextDouble() * 0.5;
            row.RecordFailure($"Dispatch failed: {exception.GetType().Name}", timeProvider.GetUtcNow().AddMilliseconds(milliseconds));
            logger.LogWarning("Outbox message {MessageId} deferred after {FailureType}; attempt {RetryCount}",
                row.Id, exception.GetType().Name, row.RetryCount);
            // Save failures propagate. A failed transaction is never reused by another attempt.
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return false;
        }

        row.MarkAsProcessed(timeProvider.GetUtcNow());
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Outbox message {MessageId} publication confirmed", row.Id);
        return true;
    }

    private static IIntegrationMessage Deserialize(OutboxMessage row, IIntegrationMessageSerializer serializer)
    {
        IIntegrationMessage message = row.MessageType switch
        {
            EarlyWarningRaisedV1.TypeName => serializer.Deserialize<EarlyWarningRaisedV1>(row.Payload),
            RiskAnalysisRequestedEvent.TypeName => serializer.Deserialize<RiskAnalysisRequestedEvent>(row.Payload),
            JitRiskAnalysisRequestedEvent.TypeName => throw new NotSupportedException("Legacy wrapped JIT requests require explicit migration."),
            RiskAnalysisRequest.TypeName => serializer.Deserialize<RiskAnalysisRequest>(row.Payload) with
            {
                MessageId = row.Id,
                OccurredAtUtc = row.OccurredAtUtc
            },
            _ => throw new NotSupportedException("The outbox message type is not supported.")
        };
        if (message.MessageId != row.Id || message.MessageType != row.MessageType ||
            message.CorrelationId != row.CorrelationId || message.OccurredAtUtc != row.OccurredAtUtc)
            throw new InvalidOperationException("Integration message metadata does not match the outbox row.");
        return message;
    }
}
