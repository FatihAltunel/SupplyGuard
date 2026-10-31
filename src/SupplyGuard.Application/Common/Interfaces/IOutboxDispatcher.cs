namespace SupplyGuard.Application.Common.Interfaces;

public interface IOutboxDispatcher
{
    /// <summary>
    /// Makes one bounded attempt to publish an already committed outbox row using
    /// the singleton publisher. Returns true for confirmed/already confirmed publication,
    /// false when deferred. Shares database row claiming with background dispatch and
    /// uses an independent unit of work. Retries preserve the original message ID.
    /// </summary>
    Task<bool> TryDispatchAsync(Guid messageId, CancellationToken cancellationToken = default);
}
