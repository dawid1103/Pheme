# Caching queries

A query marked as cacheable is answered from a cache for a while, without its handler. The query gives the key and the
duration, and a behavior on `HybridCache` of `Microsoft.Extensions.Caching.Hybrid` does the rest, handling concurrent
queries of one key once
([`ICachedQuery.cs`](https://github.com/dawid1103/Pheme/blob/master/examples/ModularMonolith/Shop.Common/Caching/ICachedQuery.cs),
[`QueryCachingBehavior.cs`](https://github.com/dawid1103/Pheme/blob/master/examples/ModularMonolith/Shop.Api/Behaviors/QueryCachingBehavior.cs)):

```csharp
public interface ICachedQuery
{
    string CacheKey { get; }

    static abstract TimeSpan CacheDuration { get; }
}

public sealed record GetProductStockQuery(string Product) : IQuery<ProductStockDto>, ICachedQuery
{
    public string CacheKey => $"ordering:stock:{Product}";

    public static TimeSpan CacheDuration => TimeSpan.FromSeconds(30);
}

internal sealed class QueryCachingBehavior<TQuery, TResponse>(HybridCache cache) : IPipelineBehavior<TQuery, TResponse>
    where TQuery : IQuery<TResponse>, ICachedQuery
{
    private static readonly HybridCacheEntryOptions EntryOptions = new()
    {
        Expiration = TQuery.CacheDuration,
        LocalCacheExpiration = TQuery.CacheDuration,
    };

    public Task<TResponse> Handle(TQuery message, MessageHandlerDelegate<TResponse> next, CancellationToken cancellationToken) =>
        cache.GetOrCreateAsync(
            message.CacheKey, next, static (next, token) => new ValueTask<TResponse>(next(token)), EntryOptions,
            cancellationToken: cancellationToken).AsTask();
}

builder.Services.AddHybridCache();
options.AddFluentValidation(...);
options.AddBehavior(typeof(QueryCachingBehavior<,>));   // after validation: an invalid query is never cached
```

In the example, `GET /stocks/{product}` may show a stock up to 30 seconds old
([`GetStock`](https://github.com/dawid1103/Pheme/tree/master/examples/ModularMonolith/Ordering.Application/Inventory/GetStock)); placing an order reads the stock itself. A failed query
is not cached. A response that depends on the user includes the user in the key, and one that must never be stale is
not cached at all - or, for changes made by the same application, the command removes the key with
`HybridCache.RemoveAsync` once its change is saved.

---

Previous: [Authorization](authorization.md) · [Contents](../README.md) · Next: [Idempotent commands](idempotent-commands.md)
