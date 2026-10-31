using SupplyGuard.Application.Common.Messaging;

namespace SupplyGuard.Application.Common.Interfaces;

public interface IOutboxReader
{
    /// <summary>
    /// Reads the retained snapshot whether publication is pending or complete.
    /// Request snapshots must survive until their terminal analysis has been audited.
    /// </summary>
    Task<OutboxMessage?> GetByMessageIdAsync(Guid messageId, CancellationToken cancellationToken = default);
}
