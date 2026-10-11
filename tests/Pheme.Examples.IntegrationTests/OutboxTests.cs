using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ordering.Domain.Orders.Events;
using Ordering.Infrastructure;
using Shipping.Domain;
using Shop.Common.Contracts;

namespace Pheme.Examples.IntegrationTests
{
    /// <summary>
    /// Integration events through the ordering module's outbox: written by a domain event handler in the transaction of
    /// the order, delivered after it is saved - the placement to a handler in each module, the cancellation to the
    /// shipping module. The tests deliver with <c>DrainOutbox</c> of <c>Pheme.Testing</c>; one runs the outbox processor
    /// in the background instead.
    /// </summary>
    [Collection(typeof(ShopCollection))]
    public class OutboxTests(ShopFixture shop)
    {
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task Placing_an_order_writes_an_integration_event_that_prepares_the_shipment_once_delivered()
        {
            using HttpClient client = shop.Factory.CreateClient();

            Guid orderId = await client.PlaceOrder("Teapot", quantity: 2, express: true);

            OrderPlacedIntegrationEvent placed = Assert.Single(
                await shop.Factory.Services.GetOutboxEvents<OrderPlacedIntegrationEvent>(cancellationToken: Token),
                e => e.OrderId == orderId);
            Assert.Equal(("Teapot", 2, true), (placed.Product, placed.Quantity, placed.Express));
            Assert.Null(await shop.Factory.Services.FindShipment(orderId));
            Assert.Equal(0, shop.Notifier.ConfirmationsOf(orderId));

            await shop.Factory.Services.DrainOutbox(cancellationToken: Token);

            Shipment? shipment = await shop.Factory.Services.FindShipment(orderId);
            Assert.Equal((ShipmentStatus.Preparing, true), (shipment?.Status, shipment?.Express));
            Assert.Equal(1, shop.Carrier.PickupRequestsOf(orderId));
            Assert.Equal(1, shop.Notifier.ConfirmationsOf(orderId));
        }

        [Fact]
        public async Task Cancelling_an_order_cancels_its_shipment_once_the_integration_event_is_delivered()
        {
            using HttpClient client = shop.Factory.CreateClient();
            Guid orderId = await client.PlaceOrder();
            await shop.Factory.Services.DrainOutbox(cancellationToken: Token);

            using HttpResponseMessage response = await client.CancelOrder(orderId);

            // The ordering module saved the cancellation and its integration event; the shipping module has not seen it.
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.Single(
                await shop.Factory.Services.GetOutboxEvents<OrderCancelledIntegrationEvent>(source: "Ordering", cancellationToken: Token),
                e => e.OrderId == orderId);
            Assert.Equal(ShipmentStatus.Preparing, (await shop.Factory.Services.FindShipment(orderId))?.Status);

            await shop.Factory.Services.DrainOutbox(cancellationToken: Token);

            Shipment? shipment = await shop.Factory.Services.FindShipment(orderId);
            Assert.Equal(ShipmentStatus.Cancelled, shipment?.Status);
            Assert.Equal([$"Return the items of order {orderId} to the shelves."], await shop.Factory.Services.GetWarehouseNotices(shipment!.Id));
        }

        [Fact]
        public async Task A_redelivered_cancellation_changes_nothing_more()
        {
            using HttpClient client = shop.Factory.CreateClient();
            Guid orderId = await client.PlaceOrder();
            await shop.Factory.Services.DrainOutbox(cancellationToken: Token);
            using HttpResponseMessage response = await client.CancelOrder(orderId);
            await shop.Factory.Services.DrainOutbox(cancellationToken: Token);

            int redelivered = await shop.Factory.Services.Redeliver<OrderCancelledIntegrationEvent>(
                e => e.OrderId == orderId, Token);
            await shop.Factory.Services.DrainOutbox(cancellationToken: Token);

            Assert.Equal(1, redelivered);
            Shipment? shipment = await shop.Factory.Services.FindShipment(orderId);
            Assert.Equal(ShipmentStatus.Cancelled, shipment?.Status);
            Assert.Single(await shop.Factory.Services.GetWarehouseNotices(shipment!.Id));
        }

        [Fact]
        public async Task An_order_cancelled_before_its_placement_is_delivered_is_not_confirmed_to_the_customer()
        {
            using HttpClient client = shop.Factory.CreateClient();
            Guid orderId = await client.PlaceOrder();

            using HttpResponseMessage response = await client.CancelOrder(orderId);
            await shop.Factory.Services.DrainOutbox(cancellationToken: Token);

            // The placement is delivered first: shipping, which does not read the orders of the other module, prepares
            // the shipment, and the cancellation delivered next cancels it.
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.Equal(0, shop.Notifier.ConfirmationsOf(orderId));
            Assert.Equal(ShipmentStatus.Cancelled, (await shop.Factory.Services.FindShipment(orderId))?.Status);
        }

        [Fact]
        public async Task A_cancellation_delivered_while_the_placement_still_fails_gets_no_shipment_prepared()
        {
            // A database of its own: each drain is one attempt at the test's messages only, none of other tests.
            await using WebApplicationFactory<Program> application = shop.CreateFactoryWithOwnDatabase();
            using HttpClient client = application.CreateClient();
            Guid orderId = await client.PlaceOrder();
            shop.Carrier.Fail(orderId, times: 1);
            using HttpResponseMessage response = await client.CancelOrder(orderId);

            // The placement fails at the carrier, so the cancellation, delivered after it, finds no shipment: shipping
            // saves the shipment cancelled, and the retried placement prepares nothing.
            await Assert.ThrowsAsync<InvalidOperationException>(() => application.Services.DrainOutbox(cancellationToken: Token));
            await application.Services.DrainOutbox(cancellationToken: Token);

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Shipment? shipment = await application.Services.FindShipment(orderId);
            Assert.Equal(ShipmentStatus.Cancelled, shipment?.Status);
            Assert.Equal(1, shop.Carrier.PickupRequestsOf(orderId));

            // Nothing was prepared, so the warehouse gets nothing to return to the shelves.
            Assert.Empty(await application.Services.GetWarehouseNotices(shipment!.Id));
        }

        [Fact]
        public async Task A_message_written_before_a_property_was_added_is_delivered_with_its_default()
        {
            using HttpClient client = shop.Factory.CreateClient();
            Guid orderId = await client.PlaceOrder();

            // The message as the version before Express wrote it: the same name, JSON without the property.
            OrderPlacedIntegrationEvent placed = Assert.Single(
                await shop.Factory.Services.GetOutboxEvents<OrderPlacedIntegrationEvent>(cancellationToken: Token),
                e => e.OrderId == orderId);
            await using (AsyncServiceScope scope = shop.Factory.Services.CreateAsyncScope())
            {
                OrderingDbContext ordering = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();
                OutboxMessage message = await ordering.Set<OutboxMessage>().SingleAsync(m => m.Id == placed.EventId, Token);
                message.Content = JsonSerializer.Serialize(
                    new { placed.EventId, placed.OccurredOn, placed.OrderId, placed.Product, placed.Quantity },
                    new JsonSerializerOptions(JsonSerializerDefaults.Web));
                Assert.DoesNotContain("express", message.Content, StringComparison.OrdinalIgnoreCase);
                await ordering.SaveChangesAsync(Token);
            }

            await shop.Factory.Services.DrainOutbox(cancellationToken: Token);

            Shipment? shipment = await shop.Factory.Services.FindShipment(orderId);
            Assert.Equal((ShipmentStatus.Preparing, false), (shipment?.Status, shipment?.Express));
            Assert.Equal(1, shop.Notifier.ConfirmationsOf(orderId));
        }

        [Fact]
        public async Task An_order_rejected_after_its_integration_event_was_added_leaves_nothing_in_the_outbox()
        {
            using HttpClient client = shop.Factory.CreateClient();
            string product = await shop.Factory.Services.CreateStock(available: 2);

            using HttpResponseMessage response = await client.PostOrder(new { product, quantity = 3 });

            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
            Assert.DoesNotContain(
                await shop.Factory.Services.GetOutboxEvents<OrderPlacedIntegrationEvent>(cancellationToken: Token),
                e => e.Product == product);
        }

        [Fact]
        public async Task The_integration_event_is_added_before_the_stock_allocation_that_rejects_an_order()
        {
            // Keeps the test above meaningful: the writer runs, and the failure after it discards the message.
            await using AsyncServiceScope scope = shop.Factory.Services.CreateAsyncScope();

            IEnumerable<IDomainEventHandler<OrderPlacedDomainEvent>> handlers =
                scope.ServiceProvider.GetServices<IDomainEventHandler<OrderPlacedDomainEvent>>();

            Assert.Equal(
                [
                    "RecordOrderHistoryDomainEventHandler",
                    "WriteOrderPlacedToOutboxDomainEventHandler",
                    "AllocateStockDomainEventHandler",
                ],
                handlers.Select(handler => handler.GetType().Name));
        }

        [Fact]
        public async Task A_failed_handler_is_retried_alone_while_the_handler_of_the_other_module_ran_once()
        {
            using HttpClient client = shop.Factory.CreateClient();
            Guid orderId = await client.PlaceOrder();
            shop.Carrier.Fail(orderId, times: 1);

            InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
                () => shop.Factory.Services.DrainOutbox(cancellationToken: Token));

            Assert.Contains("'ordering.order-placed'", failure.Message, StringComparison.Ordinal);
            Assert.Contains($"the pickup of order {orderId} is not booked", failure.Message, StringComparison.Ordinal);
            Assert.Null(await shop.Factory.Services.FindShipment(orderId));
            Assert.Equal(1, shop.Notifier.ConfirmationsOf(orderId));

            await shop.Factory.Services.DrainOutbox(cancellationToken: Token);

            Assert.Equal(ShipmentStatus.Preparing, (await shop.Factory.Services.FindShipment(orderId))?.Status);
            Assert.Equal(2, shop.Carrier.PickupRequestsOf(orderId));
            Assert.Equal(1, shop.Notifier.ConfirmationsOf(orderId));
        }

        [Fact]
        public async Task The_outbox_processor_delivers_in_the_background_without_the_test_helpers()
        {
            await using WebApplicationFactory<Program> application = shop.CreateFactoryWithOutboxProcessor();
            using HttpClient client = application.CreateClient();

            Guid orderId = await client.PlaceOrder();

            // The processor polls every 100 ms; the delay throws when the delivery takes more than 10 s.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            while (shop.Notifier.ConfirmationsOf(orderId) == 0 || await shop.Factory.Services.FindShipment(orderId) is null)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), timeout.Token);
            }

            Assert.Equal(ShipmentStatus.Preparing, (await shop.Factory.Services.FindShipment(orderId))?.Status);
            Assert.Equal(1, shop.Notifier.ConfirmationsOf(orderId));
        }
    }
}
