using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using SupplyGuard.Application.Common.Messaging;
using SupplyGuard.Infrastructure.Messaging.RabbitMq;
using Xunit;

namespace SupplyGuard.IntegrationTests;

public class EarlyWarningNotificationConsumerTests
{
    [Fact]
    public async Task PublisherToConsumer_LogsAllFieldsBeforeAck_UsingDedicatedChannelsAndBoundedPrefetch()
    {
        await using var harness = new ConsumerHarness();
        await harness.StartAsync();
        var session = harness.Sessions[0];
        await session.Ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var publisherConnection = new Mock<IConnection>();
        var publisherChannel = new Mock<IChannel>();
        publisherConnection.SetupGet(connection => connection.IsOpen).Returns(true);
        publisherChannel.SetupGet(channel => channel.IsOpen).Returns(true);
        harness.Factory.Setup(factory => factory.CreateConnectionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(publisherConnection.Object);
        publisherConnection.Setup(connection => connection.CreateChannelAsync(
            It.IsAny<CreateChannelOptions>(), It.IsAny<CancellationToken>())).ReturnsAsync(publisherChannel.Object);
        publisherChannel.Setup(channel => channel.BasicPublishAsync(It.IsAny<string>(), It.IsAny<string>(), true,
            It.IsAny<BasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
            .Returns((string _, string routingKey, bool _, BasicProperties properties, ReadOnlyMemory<byte> body,
                CancellationToken _) => new ValueTask(session.DeliverAsync(body, properties, routingKey)));
        session.Receiving.Setup(channel => channel.BasicAckAsync(42, false, It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                Assert.Contains(harness.Logger.Entries, entry => entry.Text.StartsWith("[Notification Sent]", StringComparison.Ordinal));
                return ValueTask.CompletedTask;
            });
        await using var publisher = new RabbitMqMessagePublisher(harness.Factory.Object, Options.Create(harness.Settings),
            TimeProvider.System, NullLogger<RabbitMqMessagePublisher>.Instance);
        var message = CreateMessage();

        await publisher.PublishAsync(message);
        await harness.Logger.Acknowledged.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var log = Assert.Single(harness.Logger.Entries.Where(entry => entry.Text.StartsWith("[Notification Sent]", StringComparison.Ordinal)));
        Assert.Equal(message.MessageId, log.Fields["MessageId"]);
        Assert.Equal(message.WarningId, log.Fields["WarningId"]);
        Assert.Equal(message.SupplierId, log.Fields["SupplierId"]);
        Assert.Equal(message.Severity, log.Fields["Severity"]);
        session.Receiving.Verify(channel => channel.BasicQosAsync(0, 10, false, It.IsAny<CancellationToken>()), Times.Once);
        session.Receiving.Verify(channel => channel.BasicConsumeAsync("notification.queue", false, "", false, false, null,
            It.IsAny<IAsyncBasicConsumer>(), It.IsAny<CancellationToken>()), Times.Once);
        session.Receiving.Verify(channel => channel.BasicAckAsync(42, false, It.IsAny<CancellationToken>()), Times.Once);
        session.Receiving.Verify(channel => channel.BasicNackAsync(It.IsAny<ulong>(), It.IsAny<bool>(), It.IsAny<bool>(),
            It.IsAny<CancellationToken>()), Times.Never);
        Assert.NotSame(publisherChannel.Object, session.Receiver!.Channel);
        Assert.True(session.ErrorChannelOptions!.PublisherConfirmationsEnabled);
        Assert.True(session.ErrorChannelOptions.PublisherConfirmationTrackingEnabled);
    }

    [Theory]
    [InlineData("malformed")]
    [InlineData("missing-fields")]
    [InlineData("metadata-mismatch")]
    public async Task InvalidDelivery_IsQuarantinedWithConfirmation_BeforeNackWithoutRequeue(string scenario)
    {
        await using var harness = new ConsumerHarness();
        var session = harness.Sessions[0];
        var confirmation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var errorPublicationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.ErrorPublishOutcome = token =>
        {
            errorPublicationStarted.TrySetResult();
            return new ValueTask(confirmation.Task.WaitAsync(token));
        };
        await harness.StartAsync();
        await session.Ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var message = CreateMessage();
        var body = scenario switch
        {
            "malformed" => Encoding.UTF8.GetBytes("{broken"),
            "missing-fields" => Encoding.UTF8.GetBytes("{}"),
            _ => JsonSerializer.SerializeToUtf8Bytes(message)
        };
        var properties = CreateProperties(message);
        if (scenario == "metadata-mismatch")
        {
            properties.MessageId = Guid.NewGuid().ToString("D");
        }

        var delivery = session.DeliverAsync(body, properties);
        await errorPublicationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        session.Receiving.Verify(channel => channel.BasicNackAsync(It.IsAny<ulong>(), It.IsAny<bool>(), It.IsAny<bool>(),
            It.IsAny<CancellationToken>()), Times.Never);
        confirmation.TrySetResult();
        await delivery.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(body, session.ErrorBody);
        Assert.True(session.ErrorProperties!.Persistent);
        Assert.Equal(properties.MessageId, session.ErrorProperties.MessageId);
        Assert.Equal("JsonException", session.ErrorProperties.Headers!["x-failure-type"]);
        session.Receiving.Verify(channel => channel.BasicNackAsync(42, false, false, It.IsAny<CancellationToken>()), Times.Once);
        session.Receiving.Verify(channel => channel.BasicAckAsync(It.IsAny<ulong>(), It.IsAny<bool>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessingFailure_IsRetainedInErrorQueue_WithoutAcknowledgingSuccess()
    {
        await using var harness = new ConsumerHarness();
        harness.Logger.FailNotificationProcessing = true;
        await harness.StartAsync();
        var session = harness.Sessions[0];
        await session.Ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var message = CreateMessage();

        await session.DeliverAsync(JsonSerializer.SerializeToUtf8Bytes(message), CreateProperties(message));

        Assert.Equal("InvalidOperationException", session.ErrorProperties!.Headers!["x-failure-type"]);
        session.Receiving.Verify(channel => channel.BasicNackAsync(42, false, false, It.IsAny<CancellationToken>()), Times.Once);
        session.Receiving.Verify(channel => channel.BasicAckAsync(It.IsAny<ulong>(), It.IsAny<bool>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("connection-shutdown")]
    [InlineData("channel-shutdown")]
    [InlineData("server-cancellation")]
    [InlineData("ack-failure")]
    [InlineData("quarantine-failure")]
    public async Task InterruptedSession_IsDisposed_AndFreshConsumerIsRegistered(string reason)
    {
        await using var harness = new ConsumerHarness(sessionCount: 2);
        await harness.StartAsync();
        var first = harness.Sessions[0];
        await first.Ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var shutdown = new ShutdownEventArgs(ShutdownInitiator.Peer, 320, "Broker disconnected");
        switch (reason)
        {
            case "connection-shutdown":
                first.Connection.Raise(connection => connection.ConnectionShutdownAsync += null, first.Connection.Object, shutdown);
                break;
            case "channel-shutdown":
                first.Receiving.Raise(channel => channel.ChannelShutdownAsync += null, first.Receiving.Object, shutdown);
                break;
            case "server-cancellation":
                await first.Receiver!.HandleBasicCancelAsync("notification-test");
                break;
            case "ack-failure":
                first.Receiving.Setup(channel => channel.BasicAckAsync(42, false, It.IsAny<CancellationToken>()))
                    .Returns(ValueTask.FromException(new IOException("Disconnected before ACK")));
                var message = CreateMessage();
                await first.DeliverAsync(JsonSerializer.SerializeToUtf8Bytes(message), CreateProperties(message));
                break;
            default:
                first.ErrorPublishOutcome = _ => ValueTask.FromException(new IOException("Quarantine publication failed"));
                await first.DeliverAsync(Encoding.UTF8.GetBytes("{broken"), CreateProperties(CreateMessage()));
                first.Receiving.Verify(channel => channel.BasicNackAsync(It.IsAny<ulong>(), It.IsAny<bool>(), It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()), Times.Never);
                break;
        }

        var second = harness.Sessions[1];
        await second.Ready.Task.WaitAsync(TimeSpan.FromSeconds(5));

        first.Connection.Verify(connection => connection.DisposeAsync(), Times.Once);
        first.Receiving.Verify(channel => channel.DisposeAsync(), Times.Once);
        first.Errors.Verify(channel => channel.DisposeAsync(), Times.Once);
        second.Receiving.Verify(channel => channel.BasicQosAsync(0, 10, false, It.IsAny<CancellationToken>()), Times.Once);
        Assert.NotSame(first.Receiver, second.Receiver);
    }

    [Fact]
    public async Task InitialConnectionFailure_IsRetriedWithoutStoppingTheWorker()
    {
        await using var harness = new ConsumerHarness();
        harness.Factory.SetupSequence(factory => factory.CreateConnectionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("Broker unavailable"))
            .ReturnsAsync(harness.Sessions[0].Connection.Object);

        await harness.StartAsync();
        await harness.Sessions[0].Ready.Task.WaitAsync(TimeSpan.FromSeconds(5));

        harness.Factory.Verify(factory => factory.CreateConnectionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task ShutdownDuringQuarantine_LeavesOriginalUnsettledForBrokerRedelivery()
    {
        await using var harness = new ConsumerHarness();
        var session = harness.Sessions[0];
        var publishing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.ErrorPublishOutcome = token =>
        {
            publishing.TrySetResult();
            return new ValueTask(Task.Delay(Timeout.InfiniteTimeSpan, token));
        };
        await harness.StartAsync();
        await session.Ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var delivery = session.DeliverAsync(Encoding.UTF8.GetBytes("{broken"), CreateProperties(CreateMessage()));
        await publishing.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await harness.StopAsync();
        await delivery.WaitAsync(TimeSpan.FromSeconds(5));

        session.Receiving.Verify(channel => channel.BasicNackAsync(It.IsAny<ulong>(), It.IsAny<bool>(), It.IsAny<bool>(),
            It.IsAny<CancellationToken>()), Times.Never);
        session.Receiving.Verify(channel => channel.BasicAckAsync(It.IsAny<ulong>(), It.IsAny<bool>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    private static EarlyWarningRaisedV1 CreateMessage() => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, "Critical", "consumer-test",
        new DateTimeOffset(2026, 10, 18, 9, 0, 0, TimeSpan.Zero));

    private static BasicProperties CreateProperties(EarlyWarningRaisedV1 message) => new()
    {
        MessageId = message.MessageId.ToString("D"), Type = message.MessageType,
        CorrelationId = message.CorrelationId, ContentType = "application/json", Persistent = true
    };

    private sealed class ConsumerHarness : IAsyncDisposable
    {
        public Mock<IConnectionFactory> Factory { get; } = new();
        public RabbitMqOptions Settings { get; } = new() { ConsumerReconnectDelay = TimeSpan.FromMilliseconds(10) };
        public NotificationLogCollector<EarlyWarningNotificationConsumer> Logger { get; } = new();
        public ConsumerSession[] Sessions { get; }
        private readonly EarlyWarningNotificationConsumer _worker;

        public ConsumerHarness(int sessionCount = 1)
        {
            Sessions = Enumerable.Range(0, sessionCount).Select(_ => new ConsumerSession()).ToArray();
            var attempts = 0;
            Factory.Setup(factory => factory.CreateConnectionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(() => Task.FromResult(Sessions[Math.Min(Interlocked.Increment(ref attempts) - 1, Sessions.Length - 1)].Connection.Object));
            _worker = new EarlyWarningNotificationConsumer(Factory.Object, Options.Create(Settings), TimeProvider.System, Logger);
        }

        public Task StartAsync() => _worker.StartAsync(CancellationToken.None);
        public async Task StopAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _worker.StopAsync(timeout.Token);
        }
        public async ValueTask DisposeAsync()
        {
            await StopAsync();
            _worker.Dispose();
        }
    }

    private sealed class ConsumerSession
    {
        public Mock<IConnection> Connection { get; } = new();
        public Mock<IChannel> Receiving { get; } = new();
        public Mock<IChannel> Errors { get; } = new();
        public TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public AsyncEventingBasicConsumer? Receiver { get; private set; }
        public CreateChannelOptions? ErrorChannelOptions { get; private set; }
        public BasicProperties? ErrorProperties { get; private set; }
        public byte[] ErrorBody { get; private set; } = [];
        public Func<CancellationToken, ValueTask> ErrorPublishOutcome { get; set; } = _ => ValueTask.CompletedTask;

        public ConsumerSession()
        {
            Connection.Setup(connection => connection.CreateChannelAsync(It.IsAny<CreateChannelOptions>(), It.IsAny<CancellationToken>()))
                .Returns((CreateChannelOptions options, CancellationToken _) =>
                {
                    if (options.PublisherConfirmationsEnabled)
                    {
                        ErrorChannelOptions = options;
                        return Task.FromResult(Errors.Object);
                    }
                    return Task.FromResult(Receiving.Object);
                });
            Receiving.Setup(channel => channel.BasicConsumeAsync(It.IsAny<string>(), false, "", false, false, null,
                It.IsAny<IAsyncBasicConsumer>(), It.IsAny<CancellationToken>()))
                .Returns(async (string _, bool _, string _, bool _, bool _, IDictionary<string, object?>? _,
                    IAsyncBasicConsumer consumer, CancellationToken token) =>
                {
                    Receiver = Assert.IsType<AsyncEventingBasicConsumer>(consumer);
                    await Receiver.HandleBasicConsumeOkAsync("notification-test", token);
                    Ready.TrySetResult();
                    return "notification-test";
                });
            Errors.Setup(channel => channel.BasicPublishAsync(It.IsAny<string>(), It.IsAny<string>(), true,
                It.IsAny<BasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
                .Returns((string _, string _, bool _, BasicProperties properties, ReadOnlyMemory<byte> body, CancellationToken token) =>
                {
                    ErrorProperties = properties;
                    ErrorBody = body.ToArray();
                    return ErrorPublishOutcome(token);
                });
        }

        public Task DeliverAsync(ReadOnlyMemory<byte> body, IReadOnlyBasicProperties properties,
            string routingKey = EarlyWarningRaisedV1.TypeName) => Receiver!.HandleBasicDeliverAsync(
                "notification-test", 42, false, "supplyguard.events", routingKey, properties, body);
    }
}
