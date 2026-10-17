using RabbitMQ.Client;
using SupplyGuard.Application.Common.Messaging;

namespace SupplyGuard.Infrastructure.Messaging.RabbitMq;

internal static class RabbitMqTopology
{
    public static async Task DeclareNotificationsAsync(
        IChannel channel, RabbitMqOptions settings, CancellationToken cancellationToken)
    {
        await channel.ExchangeDeclareAsync(
            exchange: settings.ExchangeName, type: ExchangeType.Topic,
            durable: true, autoDelete: false, cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync(
            queue: settings.NotificationQueueName, durable: true, exclusive: false,
            autoDelete: false, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(
            queue: settings.NotificationQueueName, exchange: settings.ExchangeName,
            routingKey: EarlyWarningRaisedV1.TypeName, cancellationToken: cancellationToken);
    }

    public static async Task DeclareNotificationErrorsAsync(
        IChannel channel, RabbitMqOptions settings, CancellationToken cancellationToken)
    {
        await channel.ExchangeDeclareAsync(
            exchange: settings.NotificationErrorExchangeName, type: ExchangeType.Direct,
            durable: true, autoDelete: false, cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync(
            queue: settings.NotificationErrorQueueName, durable: true, exclusive: false,
            autoDelete: false, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(
            queue: settings.NotificationErrorQueueName, exchange: settings.NotificationErrorExchangeName,
            routingKey: EarlyWarningRaisedV1.TypeName, cancellationToken: cancellationToken);
    }
}
