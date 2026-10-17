using SupplyGuard.Application.Common.Messaging;

namespace SupplyGuard.Application.Common.Interfaces;

public interface IMessagePublisher
{
    /// <summary>
    /// Publishes the concrete message contract and completes only after broker confirmation.
    /// Failed or unroutable publication must throw; confirmation does not mean consumer completion.
    /// Retries must preserve the original message identifier.
    /// </summary>
    Task PublishAsync(
        IIntegrationMessage message,
        CancellationToken cancellationToken = default);
}
