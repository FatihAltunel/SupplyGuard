using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using SupplyGuard.Application.Common.Exceptions;
using SupplyGuard.Application.Common.Interfaces;
using SupplyGuard.Domain.Entities;
using SupplyGuard.Infrastructure.Persistence.Configurations;

namespace SupplyGuard.Infrastructure.Persistence;

public sealed class EfUnitOfWork(SupplyGuardDbContext dbContext) : IUnitOfWork, IAsyncDisposable
{
    private IDbContextTransaction? _transaction;

    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is not null || dbContext.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("A unit-of-work transaction is already active.");
        _transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is null) throw new InvalidOperationException("Begin the unit of work before committing.");
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await _transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: XAIAuditLogConfiguration.CorrelationIdIndexName
            })
        {
            var correlations = dbContext.ChangeTracker.Entries<XAIAuditLog>()
                .Where(entry => entry.State == EntityState.Added)
                .Select(entry => entry.Entity.CorrelationId).Distinct(StringComparer.Ordinal).ToArray();
            if (correlations.Length == 1 && Guid.TryParseExact(correlations[0], "D", out var correlationId))
                throw new DuplicateCorrelationException(correlationId, exception);
            throw;
        }
        await _transaction.DisposeAsync();
        _transaction = null;
    }

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        var transaction = _transaction;
        _transaction = null;
        try
        {
            if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
        }
        finally
        {
            try
            {
                if (transaction is not null) await transaction.DisposeAsync();
            }
            finally
            {
                dbContext.ChangeTracker.Clear();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_transaction is not null) await RollbackAsync(CancellationToken.None);
    }
}
