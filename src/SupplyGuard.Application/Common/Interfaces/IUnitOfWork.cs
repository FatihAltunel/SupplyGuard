namespace SupplyGuard.Application.Common.Interfaces;

/// <summary>Owns the transaction shared by all scoped repositories and the outbox writer.</summary>
public interface IUnitOfWork
{
    Task BeginTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves all staged changes and commits atomically. Translate only the named audit
    /// correlation uniqueness violation into DuplicateCorrelationException.
    /// </summary>
    Task CommitAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Rolls back and discards staged/tracked changes, including after a failed commit.
    /// Must be safe when the transaction has already rolled back. Scope disposal also rolls back.
    /// </summary>
    Task RollbackAsync(CancellationToken cancellationToken = default);
}
