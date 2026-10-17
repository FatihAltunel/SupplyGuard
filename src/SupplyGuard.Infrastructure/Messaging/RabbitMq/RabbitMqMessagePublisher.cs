using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using SupplyGuard.Application.Common.Interfaces;
using SupplyGuard.Application.Common.Messaging;

namespace SupplyGuard.Infrastructure.Messaging.RabbitMq;

/// <summary>Owns one long-lived connection and one serialized, confirm-enabled publishing channel.</summary>
public sealed class RabbitMqMessagePublisher(
    IConnectionFactory connectionFactory,
    IOptions<RabbitMqOptions> options,
    TimeProvider timeProvider,
    ILogger<RabbitMqMessagePublisher> logger) : IMessagePublisher, IAsyncDisposable
{
    private readonly SemaphoreSlim _publishLock = new(1, 1);
    private readonly RabbitMqOptions _settings = options.Value;
    private IConnection? _connection;
    private IChannel? _channel;
    private bool _disposed;

    public async Task PublishAsync(IIntegrationMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();
        if (message.MessageId == Guid.Empty)
        {
            throw new ArgumentException("Message identifier cannot be empty.", nameof(message));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(message.MessageType);
        ArgumentException.ThrowIfNullOrWhiteSpace(message.CorrelationId);
        var body = JsonSerializer.SerializeToUtf8Bytes(message, message.GetType());
        var properties = new BasicProperties
        {
            ContentType = "application/json",
            ContentEncoding = "utf-8",
            Persistent = true,
            MessageId = message.MessageId.ToString("D"),
            CorrelationId = message.CorrelationId,
            Type = message.MessageType,
            Timestamp = new AmqpTimestamp(message.OccurredAtUtc.ToUnixTimeSeconds())
        };

        // Also bound calls made outside the outbox worker, including time waiting for channel ownership.
        using var timeout = new CancellationTokenSource(_settings.PublishTimeout, timeProvider);
        using var publishCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var token = publishCancellation.Token;
        await _publishLock.WaitAsync(token);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var channel = await GetChannelAsync(token);

            // With both confirm options enabled, this await completes on ACK, and throws on NACK/basic.return.
            await channel.BasicPublishAsync(
                exchange: _settings.ExchangeName,
                routingKey: message.MessageType,
                mandatory: true,
                basicProperties: properties,
                body: body,
                cancellationToken: token);
        }
        catch
        {
            // Discard in-flight confirmation state on failure/timeout; let the durable outbox retry later.
            await DisposeChannelAsync();
            throw;
        }
        finally
        {
            _publishLock.Release();
        }
    }

    private async Task<IChannel> GetChannelAsync(CancellationToken cancellationToken)
    {
        if (_connection is not { IsOpen: true })
        {
            await DisposeChannelAsync();
            await DisposeConnectionAsync();
            _connection = await connectionFactory.CreateConnectionAsync(cancellationToken);
            logger.LogInformation("RabbitMQ publishing connection established");
        }

        if (_channel is not { IsOpen: true })
        {
            await DisposeChannelAsync();
            var channelOptions = new CreateChannelOptions(
                publisherConfirmationsEnabled: true,
                publisherConfirmationTrackingEnabled: true);
            _channel = await _connection.CreateChannelAsync(channelOptions, cancellationToken);

            await RabbitMqTopology.DeclareNotificationsAsync(_channel, _settings, cancellationToken);
        }

        return _channel;
    }

    public async ValueTask DisposeAsync()
    {
        await _publishLock.WaitAsync();
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            await DisposeChannelAsync();
            await DisposeConnectionAsync();
        }
        finally
        {
            // Keep the gate usable so queued calls observe the disposed state safely.
            _publishLock.Release();
        }
    }

    private async ValueTask DisposeChannelAsync()
    {
        var channel = _channel;
        _channel = null;
        if (channel is not null)
        {
            try
            {
                await channel.DisposeAsync();
            }
            catch (Exception exception)
            {
                logger.LogWarning("RabbitMQ channel cleanup failed with {FailureType}", exception.GetType().Name);
            }
        }
    }

    private async ValueTask DisposeConnectionAsync()
    {
        var connection = _connection;
        _connection = null;
        if (connection is not null)
        {
            try
            {
                await connection.DisposeAsync();
            }
            catch (Exception exception)
            {
                logger.LogWarning("RabbitMQ connection cleanup failed with {FailureType}", exception.GetType().Name);
            }
        }
    }
}
