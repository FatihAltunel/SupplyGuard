using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using SupplyGuard.Application.Common.CQRS;

namespace SupplyGuard.Application.Common.Caching;

public sealed class CachingQueryHandlerDecorator<TQuery, TResult>(
    IQueryHandler<TQuery, TResult> innerHandler,
    IDistributedCache cache,
    ILogger<CachingQueryHandlerDecorator<TQuery, TResult>> logger)
    : IQueryHandler<TQuery, TResult>
    where TQuery : IQuery<TResult>, ICacheableQuery
{
    public async Task<TResult> HandleAsync(TQuery query, CancellationToken cancellationToken = default)
    {
        try
        {
            var cachedValue = await cache.GetAsync(query.CacheKey, cancellationToken);
            if (cachedValue is not null)
            {
                var cachedResult = JsonSerializer.Deserialize<TResult>(cachedValue);
                if (cachedResult is not null)
                {
                    return cachedResult;
                }
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Failed to read cache entry {CacheKey}.", query.CacheKey);
        }

        var result = await innerHandler.HandleAsync(query, cancellationToken);
        if (result is null)
        {
            return result;
        }

        try
        {
            var serializedResult = JsonSerializer.SerializeToUtf8Bytes(result);
            await cache.SetAsync(
                query.CacheKey,
                serializedResult,
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = query.CacheDuration
                },
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Failed to write cache entry {CacheKey}.", query.CacheKey);
        }

        return result;
    }
}
