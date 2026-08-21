using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using SupplyGuard.Application.Common.CQRS;

namespace SupplyGuard.Application.Common.Caching;

public sealed class CacheInvalidatingCommandHandlerDecorator<TCommand, TResult>(
    ICommandHandler<TCommand, TResult> innerHandler,
    IDistributedCache cache,
    Func<TCommand, IEnumerable<string>> cacheKeyFactory,
    ILogger<CacheInvalidatingCommandHandlerDecorator<TCommand, TResult>> logger)
    : ICommandHandler<TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    public async Task<Result<TResult>> HandleAsync(
        TCommand command,
        CancellationToken cancellationToken = default)
    {
        var result = await innerHandler.HandleAsync(command, cancellationToken);
        if (!result.IsSuccess)
        {
            return result;
        }

        foreach (var cacheKey in cacheKeyFactory(command).Distinct(StringComparer.Ordinal))
        {
            try
            {
                await cache.RemoveAsync(cacheKey, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Failed to invalidate cache entry {CacheKey}.", cacheKey);
            }
        }

        return result;
    }
}
