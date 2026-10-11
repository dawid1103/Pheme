using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ordering.Infrastructure;

namespace Pheme.Examples.IntegrationTests
{
    /// <summary>
    /// The application's idempotency behavior: an order sent again with the same <c>Idempotency-Key</c> answers the
    /// identifier of the first one and places nothing more.
    /// </summary>
    [Collection(typeof(ShopCollection))]
    public class IdempotencyTests(ShopFixture shop)
    {
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task An_order_sent_again_with_its_idempotency_key_answers_the_first_identifier_and_places_nothing_more()
        {
            using HttpClient client = shop.Factory.CreateClient();
            string product = $"Kettle {Guid.NewGuid():N}";
            Guid key = Guid.CreateVersion7();

            Guid first = await PlaceOrder(client, product, key);
            Guid again = await PlaceOrder(client, product, key);
            Guid other = await PlaceOrder(client, product, Guid.CreateVersion7());
            Guid withoutKey = await PlaceOrder(client, product, idempotencyKey: null);

            Assert.Equal(first, again);
            Assert.Equal(3, new[] { first, other, withoutKey }.Distinct().Count());
            Assert.Equal(3, await CountOrders(shop.Factory.Services, product));
        }

        [Fact]
        public async Task Of_two_orders_with_one_key_at_once_the_later_answers_409_and_places_nothing()
        {
            var interceptor = new BeforeNextSaveInterceptor<OrderingDbContext>();
            await using WebApplicationFactory<Program> application = shop.CreateFactoryWithOwnDatabase(interceptor);
            using HttpClient client = application.CreateClient();
            string product = $"Kettle {Guid.NewGuid():N}";
            Guid key = Guid.CreateVersion7();

            // The first request has found no order with the key and is saving its own when the second one, sent in
            // between, saves an order with the same key.
            HttpResponseMessage? second = null;
            interceptor.Run(async () => second = await client.PostOrder(new { product, quantity = 1 }, user: null, key));
            using HttpResponseMessage first = await client.PostOrder(new { product, quantity = 1 }, user: null, key);

            using (second)
            {
                Assert.Equal(HttpStatusCode.Created, second?.StatusCode);
            }

            Assert.Equal(HttpStatusCode.Conflict, first.StatusCode);
            Assert.Equal(1, await CountOrders(application.Services, product));
        }

        private static async Task<Guid> PlaceOrder(HttpClient client, string product, Guid? idempotencyKey)
        {
            using HttpResponseMessage response = await client.PostOrder(new { product, quantity = 1 }, user: null, idempotencyKey);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return Assert.IsType<ShopClient.PlacedOrder>(await response.Content.ReadFromJsonAsync<ShopClient.PlacedOrder>(Token)).Id;
        }

        private static async Task<int> CountOrders(IServiceProvider services, string product)
        {
            await using AsyncServiceScope scope = services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<OrderingDbContext>().Orders.CountAsync(o => o.Product == product, Token);
        }
    }
}
