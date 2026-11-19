using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SupplyGuard.Application.Common.CQRS;
using SupplyGuard.Application.Common.Interfaces;
using SupplyGuard.Application.Common.Messaging.RiskAnalysis.Worker.V1;
using SupplyGuard.Infrastructure.Messaging.Serialization;
using SupplyGuard.Application.Features.RiskManagement.ProcessRiskAnalysisResponse;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace SupplyGuard.Infrastructure.Messaging.RabbitMq;

public sealed class RiskAnalysisResponseConsumer(
    RabbitMqConnectionRetry connectionRetry,
    IServiceScopeFactory scopeFactory,
    IRiskAnalysisResponseSerializer serializer,
    IOptions<RabbitMqOptions> options,
    TimeProvider timeProvider,
    ILogger<RiskAnalysisResponseConsumer> logger) : BackgroundService
{
    private readonly RabbitMqOptions _settings = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConsumeUntilInterruptedAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning("Risk analysis consumer session failed with {FailureType}", exception.GetType().Name);
            }

            try
            {
                await Task.Delay(_settings.ConsumerReconnectDelay, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task ConsumeUntilInterruptedAsync(CancellationToken stoppingToken)
    {
        // This connection and both channels belong to the consumer, never to the singleton publisher.
        await using var connection = await connectionRetry.ConnectAsync(stoppingToken, $"{_settings.ClientProvidedName}.RiskResponses");
        await using var channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: false, publisherConfirmationTrackingEnabled: false,
                consumerDispatchConcurrency: 1), stoppingToken);
        await using var errorChannel = await connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            stoppingToken);
        using var sessionCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var interrupted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Task OnShutdownAsync(object sender, ShutdownEventArgs reason)
        {
            interrupted.TrySetResult();
            return Task.CompletedTask;
        }

        connection.ConnectionShutdownAsync += OnShutdownAsync;
        channel.ChannelShutdownAsync += OnShutdownAsync;
        errorChannel.ChannelShutdownAsync += OnShutdownAsync;
        channel.CallbackExceptionAsync += (_, _) =>
        {
            interrupted.TrySetResult();
            return Task.CompletedTask;
        };
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.UnregisteredAsync += (_, _) =>
        {
            interrupted.TrySetResult();
            return Task.CompletedTask;
        };
        consumer.ReceivedAsync += (_, delivery) => HandleDeliveryAsync(
            channel, errorChannel, delivery, interrupted, sessionCancellation.Token);

        try
        {
            await RabbitMqTopology.DeclareWorkerAsync(channel, _settings, stoppingToken);
            await channel.BasicQosAsync(
                prefetchSize: 0, prefetchCount: _settings.ConsumerPrefetchCount,
                global: false, cancellationToken: stoppingToken);
            await channel.BasicConsumeAsync(
                queue: _settings.WorkerResponseQueueName, autoAck: false,
                consumer: consumer, cancellationToken: stoppingToken);
            logger.LogInformation("Risk analysis consumer subscribed with prefetch {PrefetchCount}", _settings.ConsumerPrefetchCount);

            // Shutdown, server cancellation, or failed settlement ends this session and causes reconnection.
            await interrupted.Task.WaitAsync(stoppingToken);
        }
        finally
        {
            sessionCancellation.Cancel();
            // Disposal closes the receiving channel and lets RabbitMQ requeue any unacknowledged deliveries.
        }
    }

    private async Task HandleDeliveryAsync(
        IChannel channel,
        IChannel errorChannel,
        BasicDeliverEventArgs delivery,
        TaskCompletionSource interrupted,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            RiskAnalysisResponse message;
            try
            {
                if (delivery.Body.Length > RiskAnalysisResponseSerializer.MaximumPayloadLength * 4)
                    throw new JsonException("Completion payload exceeds the size limit.");
                var payload = new UTF8Encoding(false, true).GetString(delivery.Body.Span);
                message = serializer.Deserialize(payload);
                if (delivery.Exchange != string.Empty || delivery.RoutingKey != _settings.WorkerResponseQueueName ||
                    (delivery.BasicProperties.CorrelationId is { } correlation && correlation != message.CorrelationId) ||
                    delivery.BasicProperties.ContentType != "application/json" ||
                    (delivery.BasicProperties.ContentEncoding is { } encoding && !encoding.Equals("utf-8", StringComparison.OrdinalIgnoreCase)))
                    throw new JsonException("AMQP metadata does not match the completion contract.");
            }
            catch (Exception exception) when (exception is JsonException or DecoderFallbackException)
            {
                await QuarantineAsync(errorChannel, delivery, exception.GetType().Name, cancellationToken);
                await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken);
                return;
            }

            Result<ProcessRiskAnalysisResponseResult> result;
            await using (var scope = scopeFactory.CreateAsyncScope())
            {
                var handler = scope.ServiceProvider.GetRequiredService<
                    ICommandHandler<ProcessRiskAnalysisResponseCommand, ProcessRiskAnalysisResponseResult>>();
                result = await handler.HandleAsync(new ProcessRiskAnalysisResponseCommand(message), cancellationToken);
            }

            if (!result.IsSuccess)
            {
                // Validation, unknown request, or mismatched metadata
                // are permanent application rejections, not transient database exceptions.
                logger.LogWarning("Risk-analysis response {CorrelationId} rejected with {FailureCodes}",
                    message.CorrelationId, string.Join(",", result.Errors.Select(error => error.Code)));
                await QuarantineAsync(errorChannel, delivery, "ApplicationRejected", cancellationToken);
                await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken);
                return;
            }

            // Includes terminal failure audits and idempotent duplicate outcomes.
            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken);
            logger.LogInformation("Risk-analysis response {CorrelationId} handled with {Disposition}",
                message.CorrelationId, result.Value!.Disposition);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Leave unsettled: closing this session lets the broker redeliver.
        }
        catch (Exception exception)
        {
            // Database, quarantine publication, or settlement failed. Dispose the
            // session and back off; a new delivery also gets a new DI/DbContext scope.
            interrupted.TrySetResult();
            logger.LogWarning("Risk-analysis delivery {DeliveryTag} interrupted after {FailureType}; restarting session",
                delivery.DeliveryTag, exception.GetType().Name);
        }
    }

    private async Task QuarantineAsync(
        IChannel channel, BasicDeliverEventArgs delivery, string failureType, CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(_settings.PublishTimeout, timeProvider);
        using var publication = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var properties = new BasicProperties
        {
            Persistent = true,
            ContentType = delivery.BasicProperties.ContentType,
            ContentEncoding = delivery.BasicProperties.ContentEncoding,
            MessageId = delivery.BasicProperties.MessageId,
            CorrelationId = delivery.BasicProperties.CorrelationId,
            Type = delivery.BasicProperties.Type,
            Timestamp = delivery.BasicProperties.Timestamp,
            Headers = new Dictionary<string, object?>
            {
                ["x-original-exchange"] = delivery.Exchange,
                ["x-original-routing-key"] = delivery.RoutingKey,
                ["x-failure-type"] = failureType
            }
        };
        await channel.BasicPublishAsync(string.Empty,
            _settings.WorkerResponseErrorQueueName, mandatory: true, properties, delivery.Body, publication.Token);
        logger.LogWarning("Risk-analysis delivery {DeliveryTag} quarantined after {FailureType}",
            delivery.DeliveryTag, failureType);
    }
}
