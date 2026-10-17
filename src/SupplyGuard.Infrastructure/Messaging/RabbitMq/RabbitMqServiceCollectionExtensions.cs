using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using SupplyGuard.Application.Common.Interfaces;

namespace SupplyGuard.Infrastructure.Messaging.RabbitMq;

public static class RabbitMqServiceCollectionExtensions
{
    public static IServiceCollection AddRabbitMqMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RabbitMqOptions>()
            .Bind(configuration.GetSection(RabbitMqOptions.SectionName))
            .Validate(settings => !string.IsNullOrWhiteSpace(settings.HostName), "RabbitMq:HostName is required.")
            .Validate(settings => settings.Port is >= 1 and <= 65_535, "RabbitMq:Port must be between 1 and 65535.")
            .Validate(settings => !string.IsNullOrWhiteSpace(settings.VirtualHost), "RabbitMq:VirtualHost is required.")
            .Validate(settings => !string.IsNullOrWhiteSpace(settings.UserName), "RabbitMq:UserName is required.")
            .Validate(settings => !string.IsNullOrWhiteSpace(settings.Password) &&
                                  !settings.Password.Contains("<YOUR_", StringComparison.OrdinalIgnoreCase),
                "Set RabbitMq:Password using User Secrets or the RabbitMq__Password environment variable.")
            .Validate(settings => IsAmqpName(settings.ExchangeName) && IsAmqpName(settings.NotificationQueueName),
                "RabbitMq exchange and queue names must contain between 1 and 255 UTF-8 bytes.")
            .Validate(settings => IsAmqpName(settings.NotificationErrorExchangeName) && IsAmqpName(settings.NotificationErrorQueueName) &&
                                  settings.NotificationErrorQueueName != settings.NotificationQueueName &&
                                  settings.NotificationErrorExchangeName != settings.ExchangeName,
                "RabbitMq error topology names must be valid and different from the normal notification topology.")
            .Validate(settings => settings.ConsumerPrefetchCount is >= 1 and <= 1_000,
                "RabbitMq:ConsumerPrefetchCount must be between 1 and 1000.")
            .Validate(settings => settings.ConsumerReconnectDelay > TimeSpan.Zero && settings.ConsumerReconnectDelay <= TimeSpan.FromMinutes(1),
                "RabbitMq:ConsumerReconnectDelay must be positive and at most one minute.")
            .Validate(settings => settings.ConnectionTimeout > TimeSpan.Zero && settings.ConnectionTimeout <= TimeSpan.FromMinutes(1) &&
                                  settings.OperationTimeout > TimeSpan.Zero && settings.OperationTimeout <= TimeSpan.FromMinutes(1) &&
                                  settings.PublishTimeout > TimeSpan.Zero && settings.PublishTimeout <= TimeSpan.FromMinutes(5),
                "RabbitMq timeouts must be positive; connection/operation timeouts are limited to one minute and publication to five minutes.")
            .ValidateOnStart();

        services.AddSingleton<IConnectionFactory>(serviceProvider =>
        {
            var settings = serviceProvider.GetRequiredService<IOptions<RabbitMqOptions>>().Value;
            return new ConnectionFactory
            {
                HostName = settings.HostName,
                Port = settings.Port,
                VirtualHost = settings.VirtualHost,
                UserName = settings.UserName,
                Password = settings.Password,
                ClientProvidedName = settings.ClientProvidedName,
                RequestedConnectionTimeout = settings.ConnectionTimeout,
                HandshakeContinuationTimeout = settings.ConnectionTimeout,
                ContinuationTimeout = settings.OperationTimeout,
                RequestedHeartbeat = TimeSpan.FromSeconds(30),
                Ssl = new SslOption { Enabled = settings.UseTls, ServerName = settings.HostName },
                // The outbox owns retry timing. Recreate closed resources explicitly on the next attempt.
                AutomaticRecoveryEnabled = false,
                TopologyRecoveryEnabled = false
            };
        });
        services.AddSingleton<IMessagePublisher, RabbitMqMessagePublisher>();
        services.AddHostedService<EarlyWarningNotificationConsumer>();

        // Register the transport before the hosted dispatcher; neither opens a connection during DI construction.
        return services.AddOutboxProcessing(configuration);
    }

    private static bool IsAmqpName(string value) =>
        !string.IsNullOrWhiteSpace(value) && Encoding.UTF8.GetByteCount(value) <= 255;
}
