using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ordering.Domain.Inventory;
using Ordering.Infrastructure;

namespace Pheme.Examples.IntegrationTests
{
    /// <summary>
    /// The application's transaction behavior: receiving stock closes the restock requests with one direct delete, which
    /// writes at once, outside <c>SaveChangesAsync</c>; the command's transaction keeps it atomic with the saved stock.
    /// </summary>
    [Collection(typeof(ShopCollection))]
    public class TransactionTests(ShopFixture shop)
    {
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task Receiving_stock_raises_it_and_closes_its_restock_requests()
        {
            using HttpClient client = shop.Factory.CreateClient();
            string product = await shop.Factory.Services.CreateStock(available: 10, reorderLevel: 5);
            await client.PlaceOrder(product, quantity: 6);   // requests a restock

            using HttpResponseMessage response = await client.ReceiveStock(product, quantity: 20);

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            (int available, int requests) = await GetStock(shop.Factory.Services, product);
            Assert.Equal(24, available);
            Assert.Equal(0, requests);
        }

        [Fact]
        public async Task A_receipt_whose_save_conflicts_answers_409_and_its_direct_delete_is_rolled_back()
        {
            var interceptor = new BeforeNextSaveInterceptor<OrderingDbContext>();
            await using WebApplicationFactory<Program> application = shop.CreateFactoryWithOwnDatabase(interceptor);
            using HttpClient client = application.CreateClient();
            string product = await application.Services.CreateStock(available: 10, reorderLevel: 5);
            await client.PlaceOrder(product, quantity: 6);   // requests a restock

            // The receipt has deleted the restock request and read 4 items when an order, placed in between, takes one.
            HttpResponseMessage? order = null;
            interceptor.Run(async () => order = await client.PostOrder(new { product, quantity = 1 }));
            using HttpResponseMessage receipt = await client.ReceiveStock(product, quantity: 20);

            using (order)
            {
                Assert.Equal(HttpStatusCode.Created, order?.StatusCode);
            }

            Assert.Equal(HttpStatusCode.Conflict, receipt.StatusCode);
            (int available, int requests) = await GetStock(application.Services, product);
            Assert.Equal(3, available);
            Assert.Equal(1, requests);
        }

        private static async Task<(int Available, int RestockRequests)> GetStock(IServiceProvider services, string product)
        {
            await using AsyncServiceScope scope = services.CreateAsyncScope();
            OrderingDbContext ordering = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();
            ProductStock stock = await ordering.ProductStocks.SingleAsync(s => s.Product == product, Token);
            return (stock.Available, await ordering.RestockRequests.CountAsync(r => r.Product == product, Token));
        }
    }
}
