using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace SupplyGuard.Infrastructure.Messaging.RabbitMq;

/// <summary>Retries connection establishment only. Publication retries remain owned by the durable outbox.</summary>
public sealed class RabbitMqConnectionRetry
{
    private readonly IConnectionFactory _factory;
    private readonly ResiliencePipeline<IConnection> _pipeline;

    public RabbitMqConnectionRetry(IConnectionFactory factory, IOptions<RabbitMqOptions> options,
        TimeProvider timeProvider, ILogger<RabbitMqConnectionRetry> logger)
    {
        _factory = factory;
        var settings = options.Value;
        _pipeline = new ResiliencePipelineBuilder<IConnection> { TimeProvider = timeProvider }
            .AddRetry(new RetryStrategyOptions<IConnection>
            {
                ShouldHandle = new PredicateBuilder<IConnection>()
                    .Handle<BrokerUnreachableException>().Handle<SocketException>().Handle<IOException>().Handle<TimeoutException>(),
                MaxRetryAttempts = settings.ConnectionRetryCount,
                Delay = settings.ConnectionRetryDelay,
                BackoffType = DelayBackoffType.Exponential,
                MaxDelay = settings.ConnectionRetryMaxDelay,
                UseJitter = settings.ConnectionRetryUseJitter,
                OnRetry = args =>
                {
                    logger.LogWarning("RabbitMQ connection attempt {Attempt} failed with {FailureType}; retry in {RetryDelay}",
                        args.AttemptNumber + 1, args.Outcome.Exception?.GetType().Name, args.RetryDelay);
                    return ValueTask.CompletedTask;
                }
            }).Build();
    }

    public ValueTask<IConnection> ConnectAsync(CancellationToken cancellationToken = default, string? connectionName = null) =>
        _pipeline.ExecuteAsync(async token => connectionName is null
            ? await _factory.CreateConnectionAsync(token)
            : await _factory.CreateConnectionAsync(connectionName, token), cancellationToken);
}
