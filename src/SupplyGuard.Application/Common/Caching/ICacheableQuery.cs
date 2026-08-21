namespace SupplyGuard.Application.Common.Caching;

public interface ICacheableQuery
{
    string CacheKey { get; }
    TimeSpan CacheDuration { get; }
}
