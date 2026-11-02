using RabbitMQ.Client;
using SupplyGuard.Application.Common.Messaging;
using SupplyGuard.Application.Common.Messaging.RiskAnalysis.V1;

namespace SupplyGuard.Infrastructure.Messaging.RabbitMq;

internal static class RabbitMqTopology
{
    public static async Task DeclareRiskAnalysisAsync(IChannel channel, RabbitMqOptions settings, CancellationToken token, bool declareExchange = true)
    {
        if (declareExchange)
            await channel.ExchangeDeclareAsync(settings.ExchangeName, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: token);
        await channel.ExchangeDeclareAsync(settings.RiskAnalysisInvalidExchangeName, ExchangeType.Direct, durable: true, autoDelete: false, cancellationToken: token);
        await channel.QueueDeclareAsync(settings.RiskAnalysisInvalidQueueName, durable: true, exclusive: false, autoDelete: false, cancellationToken: token);
        await channel.QueueBindAsync(settings.RiskAnalysisInvalidQueueName, settings.RiskAnalysisInvalidExchangeName,
            RiskAnalysisRequestedEvent.TypeName, cancellationToken: token);
        // These arguments must exactly match the Python worker's durable request queue.
        await channel.QueueDeclareAsync(settings.RiskAnalysisRequestQueueName, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = settings.RiskAnalysisInvalidExchangeName,
                ["x-dead-letter-routing-key"] = RiskAnalysisRequestedEvent.TypeName
            }, cancellationToken: token);
        await channel.QueueBindAsync(settings.RiskAnalysisRequestQueueName, settings.ExchangeName,
            RiskAnalysisRequestedEvent.TypeName, cancellationToken: token);
        // No DLX arguments: Python already declares this completion queue without them.
        await channel.QueueDeclareAsync(settings.RiskAnalysisCompletedQueueName, durable: true, exclusive: false, autoDelete: false, cancellationToken: token);
        await channel.QueueBindAsync(settings.RiskAnalysisCompletedQueueName, settings.ExchangeName,
            RiskAnalysisCompletedEvent.TypeName, cancellationToken: token);
    }

    public static async Task DeclareRiskAnalysisErrorsAsync(IChannel channel, RabbitMqOptions settings, CancellationToken token)
    {
        await channel.ExchangeDeclareAsync(settings.RiskAnalysisCompletedErrorExchangeName, ExchangeType.Direct,
            durable: true, autoDelete: false, cancellationToken: token);
        await channel.QueueDeclareAsync(settings.RiskAnalysisCompletedErrorQueueName, durable: true, exclusive: false,
            autoDelete: false, cancellationToken: token);
        await channel.QueueBindAsync(settings.RiskAnalysisCompletedErrorQueueName, settings.RiskAnalysisCompletedErrorExchangeName,
            RiskAnalysisCompletedEvent.TypeName, cancellationToken: token);
    }

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
