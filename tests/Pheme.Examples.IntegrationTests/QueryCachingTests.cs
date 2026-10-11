using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ordering.Infrastructure;

namespace Pheme.Examples.IntegrationTests
{
    /// <summary>
    /// The application's query caching behavior: the stock of a product (<c>GetProductStockQuery</c>, an
    /// <c>ICachedQuery</c>) is served from <c>HybridCache</c> for 30 seconds; queries without the interface are handled
    /// each time.
    /// </summary>
    [Collection(typeof(ShopCollection))]
    public class QueryCachingTests(ShopFixture shop)
    {
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task A_cached_query_answers_from_the_cache_although_the_data_changed()
        {
            using HttpClient client = shop.Factory.CreateClient();
            string product = await shop.Factory.Services.CreateStock(available: 10);

            ShopClient.Stock before = await ReadStock(client, product);
            await client.PlaceOrder(product, quantity: 3);
            ShopClient.Stock after = await ReadStock(client, product);

            Assert.Equal(10, before.Available);
            Assert.Equal(10, after.Available);
            await using AsyncServiceScope scope = shop.Factory.Services.CreateAsyncScope();
            Assert.Equal(7, (await scope.ServiceProvider.GetRequiredService<OrderingDbContext>().ProductStocks
                .SingleAsync(s => s.Product == product, Token)).Available);
        }

        [Fact]
        public async Task A_failed_query_is_not_cached()
        {
            using HttpClient client = shop.Factory.CreateClient();
            string product = $"Kettle {Guid.NewGuid():N}";

            using HttpResponseMessage missing = await client.GetStock(product);
            await shop.Factory.Services.CreateStock(available: 10, product: product);
            ShopClient.Stock created = await ReadStock(client, product);

            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            Assert.Equal(10, created.Available);
        }

        [Fact]
        public async Task A_query_without_the_interface_is_handled_each_time()
        {
            using HttpClient client = shop.Factory.CreateClient();
            Guid id = await client.PlaceOrder();

            ShopClient.Order placed = await ReadOrder(client, id);
            using HttpResponseMessage cancelled = await client.CancelOrder(id);
            ShopClient.Order read = await ReadOrder(client, id);

            Assert.Equal("Placed", placed.Status);
            Assert.Equal("Cancelled", read.Status);
        }

        private static async Task<ShopClient.Stock> ReadStock(HttpClient client, string product)
        {
            using HttpResponseMessage response = await client.GetStock(product);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return Assert.IsType<ShopClient.Stock>(await response.Content.ReadFromJsonAsync<ShopClient.Stock>(Token));
        }

        private static async Task<ShopClient.Order> ReadOrder(HttpClient client, Guid id)
        {
            using HttpResponseMessage response = await client.GetOrder(id);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return Assert.IsType<ShopClient.Order>(await response.Content.ReadFromJsonAsync<ShopClient.Order>(Token));
        }
    }
}
