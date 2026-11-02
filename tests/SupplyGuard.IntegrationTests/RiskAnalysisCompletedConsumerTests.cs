using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using SupplyGuard.Application.Common.CQRS;
using SupplyGuard.Application.Common.Messaging.RiskAnalysis.V1;
using SupplyGuard.Application.Features.RiskManagement.ProcessRiskAnalysisCompleted;
using SupplyGuard.Infrastructure.Messaging.RabbitMq;
using SupplyGuard.Infrastructure.Messaging.Serialization;
using Xunit;

namespace SupplyGuard.IntegrationTests;

public sealed class RiskAnalysisCompletedConsumerTests
{
    [Fact]
    public async Task EveryDelivery_UsesFreshScope_DisposedBeforeAck_IncludingDuplicateAndTerminalFailure()
    {
        await using var harness = new Harness();
        var session = harness.Sessions[0];
        session.Receiving.Setup(channel => channel.BasicAckAsync(42, false, It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                Assert.Equal(harness.State.Scopes.Count, harness.State.Disposals);
                return ValueTask.CompletedTask;
            });
        await harness.StartAsync();
        var completion = RiskAnalysisFixtures.Completed();
        await session.DeliverAsync(harness.Body(completion), Properties(completion));
        harness.State.Outcome = command => Result<ProcessRiskAnalysisCompletedResult>.Success(new(
            command.Completion.SupplierId, null, RiskAnalysisCompletionDisposition.Duplicate));
        await session.DeliverAsync(harness.Body(completion), Properties(completion));
        completion = completion with { Status = "failed", Result = null, Error = new("DUMMY_PROCESSING_FAILED", "Dummy failed.") };
        harness.State.Outcome = command => Result<ProcessRiskAnalysisCompletedResult>.Success(new(
            command.Completion.SupplierId, null, RiskAnalysisCompletionDisposition.Processed));
        await session.DeliverAsync(harness.Body(completion), Properties(completion));
        Assert.Equal(3, harness.State.Scopes.Distinct().Count());
        Assert.Equal(3, harness.State.Disposals);
        session.Receiving.Verify(channel => channel.BasicAckAsync(42, false, It.IsAny<CancellationToken>()), Times.Exactly(3));
        session.Receiving.Verify(channel => channel.BasicNackAsync(It.IsAny<ulong>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        session.Receiving.Verify(channel => channel.BasicQosAsync(0, 10, false, It.IsAny<CancellationToken>()), Times.Once);
        Assert.True(session.ErrorChannelOptions!.PublisherConfirmationsEnabled);
    }

    [Theory]
    [InlineData("json")]
    [InlineData("metadata")]
    [InlineData("utf8")]
    public async Task InvalidDelivery_IsQuarantinedBeforeNack_WithoutResolvingHandler(string scenario)
    {
        await using var harness = new Harness();
        var session = harness.Sessions[0];
        var confirmation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var publishing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.ErrorPublishOutcome = token =>
        {
            publishing.TrySetResult();
            return new ValueTask(confirmation.Task.WaitAsync(token));
        };
        await harness.StartAsync();
        var completion = RiskAnalysisFixtures.Completed();
        var body = scenario == "json" ? Encoding.UTF8.GetBytes("{}") : scenario == "utf8" ? new byte[] { 255 } : harness.Body(completion);
        var properties = Properties(completion);
        if (scenario == "metadata") properties.CorrelationId = Guid.NewGuid().ToString("D");
        var delivery = session.DeliverAsync(body, properties);
        await publishing.Task.WaitAsync(TimeSpan.FromSeconds(5));
        session.Receiving.Verify(channel => channel.BasicNackAsync(42, false, false, It.IsAny<CancellationToken>()), Times.Never);
        confirmation.TrySetResult();
        await delivery.WaitAsync(TimeSpan.FromSeconds(5));
        session.Receiving.Verify(channel => channel.BasicNackAsync(42, false, false, It.IsAny<CancellationToken>()), Times.Once);
        Assert.True(session.ErrorProperties!.Persistent);
        Assert.Equal(body, session.ErrorBody);
        Assert.Empty(harness.State.Scopes);
    }

    [Fact]
    public async Task PermanentApplicationRejection_IsQuarantined()
    {
        await using var harness = new Harness();
        harness.State.Outcome = _ => Result<ProcessRiskAnalysisCompletedResult>.Failure(new Error("RiskAnalysis.RequestMismatch", "Mismatch"));
        await harness.StartAsync();
        var completed = RiskAnalysisFixtures.Completed();
        await harness.Sessions[0].DeliverAsync(harness.Body(completed), Properties(completed));
        Assert.Equal("ApplicationRejected", harness.Sessions[0].ErrorProperties!.Headers!["x-failure-type"]);
        harness.Sessions[0].Receiving.Verify(channel => channel.BasicNackAsync(42, false, false, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(1, harness.State.Disposals);
    }

    [Theory]
    [InlineData("database")]
    [InlineData("quarantine")]
    [InlineData("ack")]
    public async Task TransientOrSettlementFailure_LeavesDeliveryUnsettled_AndRestartsSession(string failure)
    {
        await using var harness = new Harness(2);
        var first = harness.Sessions[0];
        if (failure == "database") harness.State.Outcome = _ => throw new IOException("Database disconnected");
        if (failure == "quarantine") first.ErrorPublishOutcome = _ => ValueTask.FromException(new IOException("No confirm"));
        if (failure == "ack") first.Receiving.Setup(channel => channel.BasicAckAsync(42, false, It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromException(new IOException("Lost ACK")));
        await harness.StartAsync();
        var completion = RiskAnalysisFixtures.Completed();
        await first.DeliverAsync(failure == "quarantine" ? Encoding.UTF8.GetBytes("{}") : harness.Body(completion), Properties(completion));
        await harness.Sessions[1].Ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
        first.Receiving.Verify(channel => channel.BasicNackAsync(It.IsAny<ulong>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        if (failure != "ack") first.Receiving.Verify(channel => channel.BasicAckAsync(It.IsAny<ulong>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        first.Connection.Verify(connection => connection.DisposeAsync(), Times.Once);
        if (failure != "quarantine") Assert.Equal(1, harness.State.Disposals);
    }

    [Fact]
    public async Task InitialConnectionFailure_IsRetried()
    {
        await using var harness = new Harness();
        harness.Factory.SetupSequence(factory => factory.CreateConnectionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("Not ready")).ReturnsAsync(harness.Sessions[0].Connection.Object);
        await harness.StartAsync();
        harness.Factory.Verify(factory => factory.CreateConnectionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    private static BasicProperties Properties(RiskAnalysisCompletedEvent message) => new()
    {
        MessageId = message.MessageId.ToString("D"), CorrelationId = message.CorrelationId.ToString("D"),
        Type = message.MessageType, ContentType = "application/json", ContentEncoding = "utf-8", Persistent = true
    };

    private sealed class HandlerState
    {
        public List<Guid> Scopes { get; } = [];
        public int Disposals;
        public Func<ProcessRiskAnalysisCompletedCommand, Result<ProcessRiskAnalysisCompletedResult>> Outcome = command =>
            Result<ProcessRiskAnalysisCompletedResult>.Success(new(command.Completion.SupplierId, Guid.NewGuid(), RiskAnalysisCompletionDisposition.Processed));
    }

    private sealed class ScopedHandler : ICommandHandler<ProcessRiskAnalysisCompletedCommand, ProcessRiskAnalysisCompletedResult>, IDisposable
    {
        private readonly HandlerState _state;
        public ScopedHandler(HandlerState state) { _state = state; state.Scopes.Add(Guid.NewGuid()); }
        public Task<Result<ProcessRiskAnalysisCompletedResult>> HandleAsync(ProcessRiskAnalysisCompletedCommand command, CancellationToken cancellationToken = default) =>
            Task.FromResult(_state.Outcome(command));
        public void Dispose() => _state.Disposals++;
    }

    private sealed class Harness : IAsyncDisposable
    {
        public Mock<IConnectionFactory> Factory { get; } = new();
        public ConsumerSession[] Sessions { get; }
        public HandlerState State { get; } = new();
        private readonly ServiceProvider _provider;
        private readonly RiskAnalysisCompletedConsumer _worker;
        private readonly IntegrationMessageSerializer _serializer = new();
        public Harness(int count = 1)
        {
            Sessions = Enumerable.Range(0, count).Select(_ => new ConsumerSession()).ToArray();
            var attempts = 0;
            Factory.Setup(factory => factory.CreateConnectionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(() => Task.FromResult(Sessions[Math.Min(Interlocked.Increment(ref attempts) - 1, Sessions.Length - 1)].Connection.Object));
            var services = new ServiceCollection();
            services.AddSingleton(State);
            services.AddScoped<ICommandHandler<ProcessRiskAnalysisCompletedCommand, ProcessRiskAnalysisCompletedResult>, ScopedHandler>();
            _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            _worker = new(Factory.Object, _provider.GetRequiredService<IServiceScopeFactory>(), _serializer,
                Options.Create(new RabbitMqOptions { ConsumerReconnectDelay = TimeSpan.FromMilliseconds(10) }),
                TimeProvider.System, NullLogger<RiskAnalysisCompletedConsumer>.Instance);
        }
        public byte[] Body(RiskAnalysisCompletedEvent message) => Encoding.UTF8.GetBytes(_serializer.Serialize(message));
        public async Task StartAsync()
        {
            await _worker.StartAsync(CancellationToken.None);
            await Sessions[0].Ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        public async ValueTask DisposeAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _worker.StopAsync(timeout.Token);
            _worker.Dispose();
            await _provider.DisposeAsync();
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
                    await Receiver.HandleBasicConsumeOkAsync("risk-analysis-test", token);
                    Ready.TrySetResult();
                    return "risk-analysis-test";
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
            string routingKey = RiskAnalysisCompletedEvent.TypeName) => Receiver!.HandleBasicDeliverAsync(
                "risk-analysis-test", 42, false, "supplyguard.events", routingKey, properties, body);
    }
}
