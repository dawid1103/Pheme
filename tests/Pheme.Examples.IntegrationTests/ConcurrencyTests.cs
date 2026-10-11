using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ordering.Domain.Orders;
using Ordering.Infrastructure;
using Shipping.Domain;
using Shipping.Infrastructure;
using Shop.Common.Contracts;

namespace Pheme.Examples.IntegrationTests
{
    /// <summary>
    /// Two requests changing the same rows at once, with the row versions of the aggregates (PostgreSQL's <c>xmin</c>)
    /// and the unique indexes: the later save fails instead of overwriting the earlier one. A request answers 409; a
    /// delivery of the outbox is tried again and sees the change. Each test runs its own instance of the application on a database of its own,
    /// with <see cref="BeforeNextSaveInterceptor{TContext}"/> sending the other request at a set point.
    /// </summary>
    [Collection(typeof(ShopCollection))]
    public class ConcurrencyTests(ShopFixture shop)
    {
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task Two_orders_of_the_last_items_at_once_sell_no_more_than_the_stock_and_the_later_answers_409()
        {
            var interceptor = new BeforeNextSaveInterceptor<OrderingDbContext>();
            await using WebApplicationFactory<Program> application = shop.CreateFactoryWithOwnDatabase(interceptor);
            using HttpClient client = application.CreateClient();
            string product = await application.Services.CreateStock(available: 10);

            // The first order has read 10 items and allocated 6 when the second, placed in between, allocates 6 more.
            HttpResponseMessage? second = null;
            interceptor.Run(async () => second = await client.PostOrder(new { product, quantity = 6 }));
            using HttpResponseMessage first = await client.PostOrder(new { product, quantity = 6 });

            using (second)
            {
                Assert.Equal(HttpStatusCode.Created, second?.StatusCode);
            }

            Assert.Equal(HttpStatusCode.Conflict, first.StatusCode);
            await using AsyncServiceScope scope = application.Services.CreateAsyncScope();
            OrderingDbContext ordering = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();
            Assert.Equal(4, (await ordering.ProductStocks.SingleAsync(s => s.Product == product, Token)).Available);
            Assert.Equal(1, await ordering.Orders.CountAsync(o => o.Product == product, Token));
        }

        [Fact]
        public async Task A_cancellation_crossing_the_delivery_of_the_dispatch_answers_409_and_the_order_stays_shipped()
        {
            var interceptor = new BeforeNextSaveInterceptor<OrderingDbContext>();
            await using WebApplicationFactory<Program> application = shop.CreateFactoryWithOwnDatabase(interceptor);
            using HttpClient client = application.CreateClient();
            Guid orderId = await client.PlaceOrder();
            await application.Services.DrainOutbox(cancellationToken: Token);
            using HttpResponseMessage dispatched = await client.DispatchShipment(orderId);

            // The cancellation has read the placed order when the outbox delivers the dispatch, marking it shipped. Its
            // save conflicts on the order's row version or on the number of its history entry, whichever comes first.
            interceptor.Run(() => application.Services.DrainOutbox(cancellationToken: Token));
            using HttpResponseMessage cancelled = await client.CancelOrder(orderId);

            Assert.Equal(HttpStatusCode.Conflict, cancelled.StatusCode);
            await using AsyncServiceScope scope = application.Services.CreateAsyncScope();
            OrderingDbContext ordering = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();
            Assert.Equal(OrderStatus.Shipped, (await ordering.Orders.SingleAsync(o => o.Id == orderId, Token)).Status);
            Assert.Empty(await application.Services.GetOutboxEvents<OrderCancelledIntegrationEvent>(cancellationToken: Token));

            // Tried again, the cancellation sees the shipped order.
            using HttpResponseMessage again = await client.CancelOrder(orderId);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, again.StatusCode);
        }

        [Fact]
        public async Task A_cancellation_delivered_while_the_shipment_is_dispatched_is_retried_and_asks_for_the_parcel_back()
        {
            var interceptor = new BeforeNextSaveInterceptor<ShippingDbContext>();
            await using WebApplicationFactory<Program> application = shop.CreateFactoryWithOwnDatabase(interceptor);
            using HttpClient client = application.CreateClient();
            Guid orderId = await client.PlaceOrder();
            await application.Services.DrainOutbox(cancellationToken: Token);
            using HttpResponseMessage cancelled = await client.CancelOrder(orderId);

            // The delivered cancellation has read the shipment being prepared when the warehouse dispatches it.
            HttpResponseMessage? dispatched = null;
            interceptor.Run(async () => dispatched = await client.DispatchShipment(orderId));
            InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
                () => application.Services.DrainOutbox(cancellationToken: Token));

            using (dispatched)
            {
                Assert.Equal(HttpStatusCode.NoContent, dispatched?.StatusCode);
            }

            Assert.Contains("'ordering.order-cancelled'", failure.Message, StringComparison.Ordinal);
            Assert.Contains(nameof(DbUpdateConcurrencyException), failure.Message, StringComparison.Ordinal);

            await application.Services.DrainOutbox(cancellationToken: Token);

            await using AsyncServiceScope scope = application.Services.CreateAsyncScope();
            ShippingDbContext shipping = scope.ServiceProvider.GetRequiredService<ShippingDbContext>();
            Shipment shipment = await shipping.Shipments.SingleAsync(s => s.OrderId == orderId, Token);
            Assert.Equal(ShipmentStatus.ReturnRequested, shipment.Status);
            Assert.Equal(
                [$"Order {orderId} was cancelled after dispatch: get the parcel back from the carrier."],
                await shipping.WarehouseNotices.Where(n => n.ShipmentId == shipment.Id).Select(n => n.Instruction).ToArrayAsync(Token));
            OrderingDbContext ordering = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();
            Assert.Equal(OrderStatus.Cancelled, (await ordering.Orders.SingleAsync(o => o.Id == orderId, Token)).Status);
        }
    }
}
