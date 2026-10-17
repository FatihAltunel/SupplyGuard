using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using SupplyGuard.Application.Common.Messaging;
using SupplyGuard.Infrastructure.Messaging.RabbitMq;
using Xunit;

namespace SupplyGuard.IntegrationTests;

public class RabbitMqLiveTests
{
    [RabbitMqLiveFact]
    public async Task RealBroker_PublisherToNotificationConsumer_AcknowledgesSuccessAndRetainsPoisonMessage()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var settings = new RabbitMqOptions
        {
            HostName = Environment.GetEnvironmentVariable("RabbitMq__HostName") ?? "localhost",
            Port = int.Parse(Environment.GetEnvironmentVariable("RabbitMq__Port") ?? "5672"),
            VirtualHost = Environment.GetEnvironmentVariable("RabbitMq__VirtualHost") ?? "supplyguard",
            UserName = Environment.GetEnvironmentVariable("RabbitMq__UserName") ?? "supplyguard",
            Password = Environment.GetEnvironmentVariable("RabbitMq__Password")
                ?? throw new InvalidOperationException("Set RabbitMq__Password before enabling the live test."),
            UseTls = bool.Parse(Environment.GetEnvironmentVariable("RabbitMq__UseTls") ?? "false"),
            ExchangeName = $"supplyguard.test.{suffix}",
            NotificationQueueName = $"supplyguard.test.{suffix}.notification",
            NotificationErrorExchangeName = $"supplyguard.test.{suffix}.errors",
            NotificationErrorQueueName = $"supplyguard.test.{suffix}.error.queue"
        };
        var factory = new ConnectionFactory
        {
            HostName = settings.HostName, Port = settings.Port, VirtualHost = settings.VirtualHost,
            UserName = settings.UserName, Password = settings.Password,
            Ssl = new SslOption { Enabled = settings.UseTls, ServerName = settings.HostName },
            AutomaticRecoveryEnabled = false
        };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using var inspectionConnection = await factory.CreateConnectionAsync(timeout.Token);
        await using var inspection = await inspectionConnection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true), timeout.Token);
        var logger = new NotificationLogCollector<EarlyWarningNotificationConsumer>();
        using var consumer = new EarlyWarningNotificationConsumer(factory, Options.Create(settings), TimeProvider.System, logger);
        await using var publisher = new RabbitMqMessagePublisher(
            factory, Options.Create(settings), TimeProvider.System, NullLogger<RabbitMqMessagePublisher>.Instance);
        var message = new EarlyWarningRaisedV1(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, "Critical", "live-consumer-test", DateTimeOffset.UtcNow);

        await consumer.StartAsync(timeout.Token);
        try
        {
            await publisher.PublishAsync(message, timeout.Token);
            await logger.Acknowledged.Task.WaitAsync(timeout.Token);
            var log = Assert.Single(logger.Entries.Where(entry => entry.Text.StartsWith("[Notification Sent]", StringComparison.Ordinal)));
            Assert.Equal(message.MessageId, log.Fields["MessageId"]);
            Assert.Equal(message.WarningId, log.Fields["WarningId"]);
            Assert.Equal(message.SupplierId, log.Fields["SupplierId"]);
            Assert.Equal(message.Severity, log.Fields["Severity"]);

            var poisonId = Guid.NewGuid().ToString("D");
            var poisonBody = System.Text.Encoding.UTF8.GetBytes("{broken");
            await inspection.BasicPublishAsync(settings.ExchangeName, EarlyWarningRaisedV1.TypeName, mandatory: true,
                basicProperties: new BasicProperties
                {
                    Persistent = true, ContentType = "application/json", MessageId = poisonId,
                    Type = EarlyWarningRaisedV1.TypeName, CorrelationId = "live-poison-test"
                }, body: poisonBody, cancellationToken: timeout.Token);
            await logger.Quarantined.Task.WaitAsync(timeout.Token);
            var rejected = await inspection.BasicGetAsync(settings.NotificationErrorQueueName, autoAck: true, timeout.Token);
            Assert.NotNull(rejected);
            Assert.Equal(poisonId, rejected!.BasicProperties.MessageId);
            Assert.True(rejected.BasicProperties.Persistent);
            Assert.Equal(poisonBody, rejected.Body.ToArray());

            await consumer.StopAsync(timeout.Token);
            // Stopping closes the consumer channel. Any unsettled delivery would now be requeued and observable.
            Assert.Null(await inspection.BasicGetAsync(settings.NotificationQueueName, autoAck: true, timeout.Token));
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await consumer.StopAsync(cleanup.Token);
            await inspection.QueueDeleteAsync(settings.NotificationQueueName, cancellationToken: cleanup.Token);
            await inspection.QueueDeleteAsync(settings.NotificationErrorQueueName, cancellationToken: cleanup.Token);
            await inspection.ExchangeDeleteAsync(settings.ExchangeName, cancellationToken: cleanup.Token);
            await inspection.ExchangeDeleteAsync(settings.NotificationErrorExchangeName, cancellationToken: cleanup.Token);
        }
    }

    [RabbitMqLiveFact]
    public async Task RealBroker_ConfirmsDurablePublication_AndRejectsUnroutableMessage()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var settings = new RabbitMqOptions
        {
            HostName = Environment.GetEnvironmentVariable("RabbitMq__HostName") ?? "localhost",
            Port = int.Parse(Environment.GetEnvironmentVariable("RabbitMq__Port") ?? "5672"),
            VirtualHost = Environment.GetEnvironmentVariable("RabbitMq__VirtualHost") ?? "supplyguard",
            UserName = Environment.GetEnvironmentVariable("RabbitMq__UserName") ?? "supplyguard",
            Password = Environment.GetEnvironmentVariable("RabbitMq__Password")
                ?? throw new InvalidOperationException("Set RabbitMq__Password before enabling the live test."),
            UseTls = bool.Parse(Environment.GetEnvironmentVariable("RabbitMq__UseTls") ?? "false"),
            ExchangeName = $"supplyguard.test.{suffix}",
            NotificationQueueName = $"supplyguard.test.{suffix}.notification"
        };
        var factory = new ConnectionFactory
        {
            HostName = settings.HostName, Port = settings.Port, VirtualHost = settings.VirtualHost,
            UserName = settings.UserName, Password = settings.Password,
            Ssl = new SslOption { Enabled = settings.UseTls, ServerName = settings.HostName },
            AutomaticRecoveryEnabled = false
        };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        await using var inspectionConnection = await factory.CreateConnectionAsync(timeout.Token);
        await using var inspection = await inspectionConnection.CreateChannelAsync(cancellationToken: timeout.Token);
        await using var publisher = new RabbitMqMessagePublisher(
            factory, Options.Create(settings), TimeProvider.System, NullLogger<RabbitMqMessagePublisher>.Instance);
        var message = new EarlyWarningRaisedV1(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, "Critical", "live-publisher-test", DateTimeOffset.UtcNow);

        try
        {
            await publisher.PublishAsync(message, timeout.Token);
            var received = await inspection.BasicGetAsync(settings.NotificationQueueName, autoAck: true, timeout.Token);
            Assert.NotNull(received);
            Assert.True(received!.BasicProperties.Persistent);
            Assert.Equal(message.MessageId.ToString("D"), received.BasicProperties.MessageId);
            Assert.Equal(message, JsonSerializer.Deserialize<EarlyWarningRaisedV1>(received.Body.Span));

            await Assert.ThrowsAsync<PublishReturnException>(() => publisher.PublishAsync(
                new UnroutableMessage(Guid.NewGuid(), "unbound.test.v1", "live-publisher-test", DateTimeOffset.UtcNow),
                timeout.Token));
        }
        finally
        {
            // Remove only the unique topology owned by this test.
            await inspection.QueueDeleteAsync(settings.NotificationQueueName, cancellationToken: timeout.Token);
            await inspection.ExchangeDeleteAsync(settings.ExchangeName, cancellationToken: timeout.Token);
        }
    }

    private sealed record UnroutableMessage(
        Guid MessageId, string MessageType, string CorrelationId, DateTimeOffset OccurredAtUtc) : IIntegrationMessage;
}

public sealed class RabbitMqLiveFactAttribute : FactAttribute
{
    public RabbitMqLiveFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("SUPPLYGUARD_RUN_RABBITMQ_TESTS") != "1")
        {
            Skip = "Set SUPPLYGUARD_RUN_RABBITMQ_TESTS=1 and RabbitMq__Password to test against a running broker.";
        }
    }
}
