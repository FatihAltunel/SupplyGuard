using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using SupplyGuard.Application.Common.Messaging;

namespace SupplyGuard.Infrastructure.Messaging.RabbitMq;

public sealed class EarlyWarningNotificationConsumer(
    IConnectionFactory connectionFactory,
    IOptions<RabbitMqOptions> options,
    TimeProvider timeProvider,
    ILogger<EarlyWarningNotificationConsumer> logger) : BackgroundService
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
                logger.LogWarning("Notification consumer session failed with {FailureType}", exception.GetType().Name);
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
        await using var connection = await connectionFactory.CreateConnectionAsync(
            $"{_settings.ClientProvidedName}.Notifications", stoppingToken);
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
            await RabbitMqTopology.DeclareNotificationsAsync(channel, _settings, stoppingToken);
            await RabbitMqTopology.DeclareNotificationErrorsAsync(errorChannel, _settings, stoppingToken);
            await channel.BasicQosAsync(
                prefetchSize: 0, prefetchCount: _settings.ConsumerPrefetchCount,
                global: false, cancellationToken: stoppingToken);
            await channel.BasicConsumeAsync(
                queue: _settings.NotificationQueueName, autoAck: false,
                consumer: consumer, cancellationToken: stoppingToken);
            logger.LogInformation("Notification consumer subscribed with prefetch {PrefetchCount}", _settings.ConsumerPrefetchCount);

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
            EarlyWarningRaisedV1 message;
            try
            {
                // The client owns Body's memory; deserialize here and never retain it after this callback returns.
                message = Deserialize(delivery);
                logger.LogInformation(
                    "[Notification Sent] MessageId: {MessageId} WarningId: {WarningId} SupplierId: {SupplierId} Severity: {Severity}",
                    message.MessageId, message.WarningId, message.SupplierId, message.Severity);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                await QuarantineAsync(errorChannel, delivery, exception, cancellationToken);
                // The original is rejected only after its durable error copy is confirmed by the broker.
                await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, cancellationToken);
                logger.LogWarning("Notification delivery {DeliveryTag} quarantined after {FailureType}",
                    delivery.DeliveryTag, exception.GetType().Name);
                return;
            }

            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken);
            logger.LogDebug("Notification message {MessageId} acknowledged", message.MessageId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Leave the delivery unacknowledged; closing this session will requeue it.
        }
        catch (Exception exception)
        {
            interrupted.TrySetResult();
            logger.LogWarning("Notification delivery {DeliveryTag} settlement failed with {FailureType}; restarting session",
                delivery.DeliveryTag, exception.GetType().Name);
        }
    }

    private async Task QuarantineAsync(
        IChannel channel, BasicDeliverEventArgs delivery, Exception exception, CancellationToken cancellationToken)
    {
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
                ["x-failure-type"] = exception.GetType().Name
            }
        };
        using var timeout = new CancellationTokenSource(_settings.PublishTimeout, timeProvider);
        using var publishCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        await channel.BasicPublishAsync(
            exchange: _settings.NotificationErrorExchangeName, routingKey: EarlyWarningRaisedV1.TypeName,
            mandatory: true, basicProperties: properties, body: delivery.Body,
            cancellationToken: publishCancellation.Token);
    }

    private static EarlyWarningRaisedV1 Deserialize(BasicDeliverEventArgs delivery)
    {
        var message = JsonSerializer.Deserialize<EarlyWarningRaisedV1>(delivery.Body.Span)
            ?? throw new JsonException("Notification payload is null.");
        if (delivery.RoutingKey != EarlyWarningRaisedV1.TypeName ||
            delivery.BasicProperties.Type != EarlyWarningRaisedV1.TypeName ||
            !Guid.TryParse(delivery.BasicProperties.MessageId, out var messageId) || messageId != message.MessageId ||
            delivery.BasicProperties.CorrelationId != message.CorrelationId ||
            message.MessageId == Guid.Empty || message.WarningId == Guid.Empty || message.SupplierId == Guid.Empty ||
            message.RiskAssessmentId == Guid.Empty || string.IsNullOrWhiteSpace(message.CorrelationId) ||
            message.OccurredAtUtc == default ||
            message.Severity is not ("Informational" or "Low" or "Medium" or "High" or "Critical"))
        {
            throw new JsonException("Notification contract or transport metadata is invalid.");
        }

        return message;
    }
}
