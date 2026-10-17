namespace SupplyGuard.Infrastructure.Messaging.RabbitMq;

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public string HostName { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string VirtualHost { get; set; } = "supplyguard";
    public string UserName { get; set; } = "supplyguard";
    public string Password { get; set; } = string.Empty;
    public bool UseTls { get; set; }
    public string ClientProvidedName { get; set; } = "SupplyGuard.OutboxPublisher";
    public string ExchangeName { get; set; } = "supplyguard.events";
    public string NotificationQueueName { get; set; } = "notification.queue";
    public string NotificationErrorExchangeName { get; set; } = "supplyguard.notification.errors";
    public string NotificationErrorQueueName { get; set; } = "notification.error.queue";
    public ushort ConsumerPrefetchCount { get; set; } = 10;
    public TimeSpan ConsumerReconnectDelay { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan OperationTimeout { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan PublishTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
