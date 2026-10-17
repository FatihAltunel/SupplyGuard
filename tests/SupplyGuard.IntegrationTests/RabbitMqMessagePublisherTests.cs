using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using SupplyGuard.Application.Common.Interfaces;
using SupplyGuard.Application.Common.Messaging;
using SupplyGuard.Infrastructure.Messaging.RabbitMq;
using SupplyGuard.Infrastructure.Persistence.Outbox;
using Xunit;

namespace SupplyGuard.IntegrationTests;

public class RabbitMqMessagePublisherTests
{
    [Fact]
    public async Task Publish_WaitsForConfirmation_AndPreservesConcretePayloadAndIdentity()
    {
        var broker = new BrokerDouble();
        var confirmed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        broker.PublishOutcome = token => new ValueTask(confirmed.Task.WaitAsync(token));
        await using var publisher = broker.CreatePublisher();
        var message = CreateMessage();

        var publication = publisher.PublishAsync(message);

        Assert.False(publication.IsCompleted);
        Assert.True(broker.ChannelOptions!.PublisherConfirmationsEnabled);
        Assert.True(broker.ChannelOptions.PublisherConfirmationTrackingEnabled);
        Assert.True(broker.Mandatory);
        Assert.Equal(EarlyWarningRaisedV1.TypeName, broker.RoutingKey);
        Assert.True(broker.Properties!.Persistent);
        Assert.Equal("application/json", broker.Properties.ContentType);
        Assert.Equal(message.MessageId.ToString("D"), broker.Properties.MessageId);
        Assert.Equal(message.CorrelationId, broker.Properties.CorrelationId);
        Assert.Equal(message.MessageType, broker.Properties.Type);
        Assert.Equal(message, JsonSerializer.Deserialize<EarlyWarningRaisedV1>(broker.Body));
        broker.Channel.Verify(channel => channel.ExchangeDeclareAsync(
            "supplyguard.events", ExchangeType.Topic, true, false, null, false, false,
            It.IsAny<CancellationToken>()), Times.Once);
        broker.Channel.Verify(channel => channel.QueueDeclareAsync(
            "notification.queue", true, false, false, null, false, false,
            It.IsAny<CancellationToken>()), Times.Once);
        broker.Channel.Verify(channel => channel.QueueBindAsync(
            "notification.queue", "supplyguard.events", EarlyWarningRaisedV1.TypeName, null, false,
            It.IsAny<CancellationToken>()), Times.Once);

        confirmed.TrySetResult();
        await publication.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ConcurrentPublication_SerializesChannelAccess_AndReusesConnectionAndChannel()
    {
        var broker = new BrokerDouble();
        var firstConfirmation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        broker.PublishOutcome = token => broker.PublishCalls == 1
            ? new ValueTask(firstConfirmation.Task.WaitAsync(token))
            : ValueTask.CompletedTask;
        await using var publisher = broker.CreatePublisher();

        var first = publisher.PublishAsync(CreateMessage());
        var second = publisher.PublishAsync(CreateMessage());
        Assert.Equal(1, broker.PublishCalls);
        Assert.False(second.IsCompleted);

        firstConfirmation.TrySetResult();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, broker.PublishCalls);
        broker.Factory.Verify(factory => factory.CreateConnectionAsync(It.IsAny<CancellationToken>()), Times.Once);
        broker.Connection.Verify(connection => connection.CreateChannelAsync(
            It.IsAny<CreateChannelOptions>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Publish_NackOrMandatoryReturn_PropagatesFailureAndRecreatesChannel(bool isReturn)
    {
        var broker = new BrokerDouble();
        var failure = new PublishException(1, isReturn);
        broker.PublishOutcome = _ => ValueTask.FromException(failure);
        await using var publisher = broker.CreatePublisher();
        var message = CreateMessage();

        var thrown = await Assert.ThrowsAsync<PublishException>(() => publisher.PublishAsync(message));

        Assert.Same(failure, thrown);
        broker.Channel.Verify(channel => channel.DisposeAsync(), Times.Once);
        broker.PublishOutcome = _ => ValueTask.CompletedTask;
        await publisher.PublishAsync(message);

        Assert.Equal(message.MessageId.ToString("D"), broker.Properties!.MessageId);
        broker.Factory.Verify(factory => factory.CreateConnectionAsync(It.IsAny<CancellationToken>()), Times.Once);
        broker.Connection.Verify(connection => connection.CreateChannelAsync(
            It.IsAny<CreateChannelOptions>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Publish_ConfirmationTimeout_FailsAndDiscardsChannel()
    {
        var broker = new BrokerDouble();
        broker.Settings.PublishTimeout = TimeSpan.FromMilliseconds(50);
        broker.PublishOutcome = token => new ValueTask(Task.Delay(Timeout.InfiniteTimeSpan, token));
        await using var publisher = broker.CreatePublisher();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => publisher.PublishAsync(CreateMessage()));

        broker.Channel.Verify(channel => channel.DisposeAsync(), Times.Once);
    }

    [Fact]
    public async Task Publish_ClosedConnection_IsReplacedOnNextAttempt_AndDisposedAtShutdown()
    {
        var broker = new BrokerDouble();
        var publisher = broker.CreatePublisher();
        await publisher.PublishAsync(CreateMessage());
        broker.Connection.SetupGet(connection => connection.IsOpen).Returns(false);
        var replacement = new Mock<IConnection>();
        replacement.SetupGet(connection => connection.IsOpen).Returns(true);
        replacement.Setup(connection => connection.CreateChannelAsync(
            It.IsAny<CreateChannelOptions>(), It.IsAny<CancellationToken>())).ReturnsAsync(broker.Channel.Object);
        broker.Factory.Setup(factory => factory.CreateConnectionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(replacement.Object);

        await publisher.PublishAsync(CreateMessage());
        await publisher.DisposeAsync();
        await publisher.DisposeAsync();

        broker.Connection.Verify(connection => connection.DisposeAsync(), Times.Once);
        replacement.Verify(connection => connection.DisposeAsync(), Times.Once);
        broker.Factory.Verify(factory => factory.CreateConnectionAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => publisher.PublishAsync(CreateMessage()));
    }

    [Fact]
    public async Task Registration_BindsSettings_RegistersSingletonPublisher_AndActivatesWorkerWithoutConnecting()
    {
        var configuration = BuildConfiguration();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRabbitMqMessaging(configuration);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        var factory = Assert.IsType<ConnectionFactory>(provider.GetRequiredService<IConnectionFactory>());
        Assert.Equal("rabbitmq.test", factory.HostName);
        Assert.Equal(5678, factory.Port);
        Assert.Equal("test-vhost", factory.VirtualHost);
        Assert.Equal("publisher-user", factory.UserName);
        Assert.True(factory.Ssl.Enabled);
        Assert.Equal("rabbitmq.test", factory.Ssl.ServerName);
        Assert.False(factory.AutomaticRecoveryEnabled);
        var hostedServices = provider.GetServices<IHostedService>().ToArray();
        Assert.Single(hostedServices.OfType<OutboxProcessor>());
        Assert.Single(hostedServices.OfType<EarlyWarningNotificationConsumer>());
        var publisher = provider.GetRequiredService<IMessagePublisher>();
        using var scope = provider.CreateScope();
        Assert.Same(publisher, scope.ServiceProvider.GetRequiredService<IMessagePublisher>());
    }

    [Theory]
    [InlineData("RabbitMq:Password", "<YOUR_RABBITMQ_PASSWORD>")]
    [InlineData("RabbitMq:Port", "70000")]
    public async Task Registration_InvalidConfiguration_IsRejected(string key, string value)
    {
        var configuration = BuildConfiguration();
        configuration[key] = value;
        var services = new ServiceCollection();
        services.AddRabbitMqMessaging(configuration);
        await using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<RabbitMqOptions>>().Value);
    }

    private static IConfigurationRoot BuildConfiguration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RabbitMq:HostName"] = "rabbitmq.test",
            ["RabbitMq:Port"] = "5678",
            ["RabbitMq:VirtualHost"] = "test-vhost",
            ["RabbitMq:UserName"] = "publisher-user",
            ["RabbitMq:Password"] = "test-only-password",
            ["RabbitMq:UseTls"] = "true"
        }).Build();

    private static EarlyWarningRaisedV1 CreateMessage() => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, "Critical", "publisher-test",
        new DateTimeOffset(2026, 10, 17, 9, 0, 0, TimeSpan.Zero));

    private sealed class BrokerDouble
    {
        public Mock<IConnectionFactory> Factory { get; } = new();
        public Mock<IConnection> Connection { get; } = new();
        public Mock<IChannel> Channel { get; } = new();
        public RabbitMqOptions Settings { get; } = new();
        public CreateChannelOptions? ChannelOptions { get; private set; }
        public BasicProperties? Properties { get; private set; }
        public byte[] Body { get; private set; } = [];
        public string? RoutingKey { get; private set; }
        public bool Mandatory { get; private set; }
        public int PublishCalls { get; private set; }
        public Func<CancellationToken, ValueTask> PublishOutcome { get; set; } = _ => ValueTask.CompletedTask;

        public BrokerDouble()
        {
            Connection.SetupGet(connection => connection.IsOpen).Returns(true);
            Channel.SetupGet(channel => channel.IsOpen).Returns(true);
            Factory.Setup(factory => factory.CreateConnectionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Connection.Object);
            Connection.Setup(connection => connection.CreateChannelAsync(
                It.IsAny<CreateChannelOptions>(), It.IsAny<CancellationToken>()))
                .Callback<CreateChannelOptions, CancellationToken>((settings, _) => ChannelOptions = settings)
                .ReturnsAsync(Channel.Object);
            Channel.Setup(channel => channel.BasicPublishAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<BasicProperties>(),
                It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
                .Returns((string _, string routingKey, bool mandatory, BasicProperties properties,
                    ReadOnlyMemory<byte> body, CancellationToken cancellationToken) =>
                {
                    PublishCalls++;
                    Properties = properties;
                    Body = body.ToArray();
                    RoutingKey = routingKey;
                    Mandatory = mandatory;
                    return PublishOutcome(cancellationToken);
                });
        }

        public RabbitMqMessagePublisher CreatePublisher() => new(
            Factory.Object, Options.Create(Settings), TimeProvider.System, NullLogger<RabbitMqMessagePublisher>.Instance);
    }
}
