using Pheme;
using Shop.Common.Caching;

namespace Ordering.Application.Inventory.GetStock
{
    /// <summary>
    /// Reads how many items of a product are available, for the shop's pages: served from the cache for up to 30
    /// seconds, so it may lag behind the orders. The allocation of an order reads the stock itself.
    /// </summary>
    public sealed record GetProductStockQuery(string Product) : IQuery<ProductStockDto>, ICachedQuery
    {
        /// <inheritdoc />
        public string CacheKey => $"ordering:stock:{Product}";

        /// <inheritdoc />
        public static TimeSpan CacheDuration => TimeSpan.FromSeconds(30);
    }
}
