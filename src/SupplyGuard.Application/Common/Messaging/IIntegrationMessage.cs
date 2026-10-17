namespace SupplyGuard.Application.Common.Messaging;

public interface IIntegrationMessage
{
    Guid MessageId { get; }
    string MessageType { get; }
    string CorrelationId { get; }
    DateTimeOffset OccurredAtUtc { get; }
}
