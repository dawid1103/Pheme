using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ordering.Application;
using Ordering.Domain.Inventory;
using Ordering.Infrastructure;
using Shipping.Domain;
using Shipping.Infrastructure;

namespace Pheme.Examples.IntegrationTests
{
    /// <summary>
    /// Domain events, published by the <c>Pheme.EntityFrameworkCore</c> interceptor before a module's <c>DbContext</c>
    /// saves: the handlers' changes are saved with the entity, or not at all. The ordering module's entities implement
    /// <c>IHasDomainEvents</c>, the shipping module's derive from <c>AggregateRoot</c>.
    /// </summary>
    [Collection(typeof(ShopCollection))]
    public class DomainEventTests(ShopFixture shop)
    {
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task Handlers_of_an_event_run_in_the_order_of_their_registrations_not_of_their_names()
        {
            using HttpClient client = shop.Factory.CreateClient();
            string product = await shop.Factory.Services.CreateStock(available: 10, reorderLevel: 2);

            Guid id = await client.PlaceOrder(product, quantity: 3);

            Assert.Equal([(1, $"Placed 3 x {product}"), (2, $"Allocated 3 x {product}")], await shop.Factory.Services.GetOrderHistory(id));
            Assert.Equal(7, (await GetStock(product)).Available);
        }

        [Fact]
        public void The_scan_alone_would_run_the_handlers_the_other_way_round()
        {
            // Keeps the test above meaningful: moving a handler to another namespace may change the order of the scan.
            Type[] handlers = [.. typeof(IOrderingDbContext).Assembly.GetTypes()
                .Where(type => type.Name is "RecordOrderHistoryDomainEventHandler" or "AllocateStockDomainEventHandler")
                .OrderBy(type => type.FullName, StringComparer.Ordinal)];

            Assert.Equal(
                ["AllocateStockDomainEventHandler", "RecordOrderHistoryDomainEventHandler"],
                handlers.Select(type => type.Name));
        }

        [Fact]
        public async Task One_handler_class_handles_both_placing_and_cancelling_an_order()
        {
            using HttpClient client = shop.Factory.CreateClient();
            Guid id = await client.PlaceOrder("Teapot", quantity: 2);

            using HttpResponseMessage cancelled = await client.CancelOrder(id);
            using HttpResponseMessage cancelledAgain = await client.CancelOrder(id);

            Assert.Equal(HttpStatusCode.NoContent, cancelled.StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, cancelledAgain.StatusCode);
            Assert.Equal([(1, "Placed 2 x Teapot"), (2, "Cancelled")], await shop.Factory.Services.GetOrderHistory(id));
        }

        [Fact]
        public async Task An_event_raised_by_a_handler_is_published_in_a_second_round_before_the_same_save()
        {
            using HttpClient client = shop.Factory.CreateClient();
            string product = await shop.Factory.Services.CreateStock(available: 10, reorderLevel: 5);

            Guid id = await client.PlaceOrder(product, quantity: 6);

            // AllocateStockDomainEventHandler raised ReorderLevelReachedDomainEvent; RequestRestockDomainEventHandler's
            // request is saved with the order. Running low is a fact about the stock, so the order's history keeps out of it.
            await using AsyncServiceScope scope = shop.Factory.Services.CreateAsyncScope();
            RestockRequest request = await scope.ServiceProvider.GetRequiredService<OrderingDbContext>().RestockRequests
                .SingleAsync(r => r.Product == product, Token);
            Assert.Equal(4, request.Available);
            Assert.Equal([(1, $"Placed 6 x {product}"), (2, $"Allocated 6 x {product}")], await shop.Factory.Services.GetOrderHistory(id));
            Assert.Equal(4, (await GetStock(product)).Available);
        }

        [Fact]
        public async Task A_failing_handler_answers_422_and_saves_neither_the_order_nor_the_changes_of_the_handlers_before_it()
        {
            using HttpClient client = shop.Factory.CreateClient();
            string product = await shop.Factory.Services.CreateStock(available: 2, reorderLevel: 0);
            int historyBefore = await CountHistory();

            using HttpResponseMessage response = await client.PostOrder(new { product, quantity = 3 });

            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
            Microsoft.AspNetCore.Mvc.ProblemDetails? problem =
                await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>(Token);
            Assert.Equal($"Only 2 items of {product} are available, not 3.", problem?.Detail);
            await using AsyncServiceScope scope = shop.Factory.Services.CreateAsyncScope();
            OrderingDbContext ordering = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();
            Assert.False(await ordering.Orders.AnyAsync(o => o.Product == product, Token));
            Assert.Equal(historyBefore, await CountHistory());
            Assert.Equal(2, (await GetStock(product)).Available);
        }

        [Fact]
        public async Task An_event_of_the_shipping_module_is_published_when_its_own_DbContext_saves()
        {
            using HttpClient client = shop.Factory.CreateClient();
            Guid orderId = await client.PlaceOrder();
            await shop.Factory.Services.DrainOutbox(cancellationToken: Token);

            // The delivered cancellation cancels the shipment, which raises ShipmentPreparationCancelledDomainEvent; its
            // handler's notice is saved with the shipment.
            using HttpResponseMessage cancelled = await client.CancelOrder(orderId);
            await shop.Factory.Services.DrainOutbox(cancellationToken: Token);

            Assert.Equal(HttpStatusCode.NoContent, cancelled.StatusCode);
            await using AsyncServiceScope scope = shop.Factory.Services.CreateAsyncScope();
            ShippingDbContext shipping = scope.ServiceProvider.GetRequiredService<ShippingDbContext>();
            Shipment shipment = await shipping.Shipments.SingleAsync(s => s.OrderId == orderId, Token);
            Assert.Equal(ShipmentStatus.Cancelled, shipment.Status);
            WarehouseNotice notice = await shipping.WarehouseNotices.SingleAsync(n => n.ShipmentId == shipment.Id, Token);
            Assert.Equal($"Return the items of order {orderId} to the shelves.", notice.Instruction);
        }

        private async Task<ProductStock> GetStock(string product)
        {
            await using AsyncServiceScope scope = shop.Factory.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<OrderingDbContext>().ProductStocks
                .SingleAsync(s => s.Product == product, Token);
        }

        private async Task<int> CountHistory()
        {
            await using AsyncServiceScope scope = shop.Factory.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<OrderingDbContext>().OrderHistory.CountAsync(Token);
        }
    }
}
