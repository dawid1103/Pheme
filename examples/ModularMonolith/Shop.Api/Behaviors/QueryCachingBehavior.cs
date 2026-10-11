using Microsoft.Extensions.Caching.Hybrid;
using Pheme;
using Shop.Common.Caching;

namespace Shop.Api.Behaviors
{
    /// <summary>
    /// Serves the response of an <see cref="ICachedQuery"/> from <see cref="HybridCache"/>, handling the query only when
    /// its key is not cached; concurrent queries of one key are handled once. A failure is not cached.
    /// </summary>
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
                message.CacheKey,
                next,
                static (next, token) => new ValueTask<TResponse>(next(token)),
                EntryOptions,
                cancellationToken: cancellationToken).AsTask();
    }
}
