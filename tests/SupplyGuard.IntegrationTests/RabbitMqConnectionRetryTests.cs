using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using SupplyGuard.Infrastructure.Messaging.RabbitMq;
using Xunit;

namespace SupplyGuard.IntegrationTests;

public sealed class RabbitMqConnectionRetryTests
{
    [Fact]
    public async Task BrokerNotReady_RetriesWithTwoFourEightSecondBackoff_ThenConnects()
    {
        var factory = new Mock<IConnectionFactory>();
        var connection = new Mock<IConnection>();
        factory.SetupSequence(x => x.CreateConnectionAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BrokerUnreachableException(new IOException("Starting")))
            .ThrowsAsync(new IOException("Starting"))
            .ThrowsAsync(new TimeoutException("Starting"))
            .ReturnsAsync(connection.Object);
        var logger = new RetryLogger();
        var retry = new RabbitMqConnectionRetry(factory.Object, Options.Create(new RabbitMqOptions()), TimeProvider.System, logger);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        Assert.Same(connection.Object, await retry.ConnectAsync(timeout.Token));
        Assert.Equal(new[] { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8) }, logger.Delays);
        factory.Verify(x => x.CreateConnectionAsync(It.IsAny<CancellationToken>()), Times.Exactly(4));
    }

    [Fact]
    public async Task Shutdown_CancelsBackoffWithoutFurtherAttempts()
    {
        var factory = new Mock<IConnectionFactory>();
        factory.Setup(x => x.CreateConnectionAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new IOException("Starting"));
        var logger = new RetryLogger();
        var retry = new RabbitMqConnectionRetry(factory.Object, Options.Create(new RabbitMqOptions()), TimeProvider.System, logger);
        using var stopping = new CancellationTokenSource();
        var connecting = retry.ConnectAsync(stopping.Token).AsTask();
        await logger.RetryStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        stopping.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connecting);
        factory.Verify(x => x.CreateConnectionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RetryExhaustion_PropagatesFailureToDurableOutbox()
    {
        var factory = new Mock<IConnectionFactory>();
        factory.Setup(x => x.CreateConnectionAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new IOException("Offline"));
        var settings = new RabbitMqOptions { ConnectionRetryDelay = TimeSpan.FromMilliseconds(1), ConnectionRetryCount = 2 };
        var retry = new RabbitMqConnectionRetry(factory.Object, Options.Create(settings), TimeProvider.System, new RetryLogger());
        await Assert.ThrowsAsync<IOException>(() => retry.ConnectAsync().AsTask());
        factory.Verify(x => x.CreateConnectionAsync(It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    private sealed class RetryLogger : ILogger<RabbitMqConnectionRetry>
    {
        public List<TimeSpan> Delays { get; } = [];
        public TaskCompletionSource RetryStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var values = (IEnumerable<KeyValuePair<string, object?>>)state!;
            Delays.Add((TimeSpan)values.Single(x => x.Key == "RetryDelay").Value!);
            RetryStarted.TrySetResult();
        }
    }
}
