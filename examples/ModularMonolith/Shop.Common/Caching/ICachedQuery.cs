namespace Shop.Common.Caching
{
    /// <summary>
    /// A query whose response may be served from the cache of the API for a while instead of being handled each time.
    /// For data that may be a little old when shown; a handler that must see the current state reads the database.
    /// </summary>
    public interface ICachedQuery
    {
        /// <summary>
        /// The key of the cached response: the same for queries asking the same, different for any other. A response
        /// that depends on the user includes the user in the key.
        /// </summary>
        string CacheKey { get; }

        /// <summary>
        /// How long a response is served from the cache after it was handled.
        /// </summary>
        static abstract TimeSpan CacheDuration { get; }
    }
}
