using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Shipping.Domain;
using Shop.Common.Contracts;

namespace Pheme.Examples.IntegrationTests
{
    /// <summary>
    /// The dispatch of a shipment, through the shipping module's outbox, the second of the application: the ordering
    /// module marks the order shipped and refuses to cancel it, and a cancellation crossing the dispatch asks for the
    /// parcel back.
    /// </summary>
    [Collection(typeof(ShopCollection))]
    public class ShipmentDispatchTests(ShopFixture shop)
    {
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task Dispatching_a_shipment_marks_its_order_shipped_once_the_integration_event_is_delivered()
        {
            using HttpClient client = shop.Factory.CreateClient();
            Guid orderId = await PlacePreparedOrder(client);

            using HttpResponseMessage response = await client.DispatchShipment(orderId);

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.Single(
                await shop.Factory.Services.GetOutboxEvents<ShipmentDispatchedIntegrationEvent>(source: "Shipping", cancellationToken: Token),
                e => e.OrderId == orderId);
            Assert.Equal("Placed", await GetOrderStatus(client, orderId));

            await shop.Factory.Services.DrainOutbox(cancellationToken: Token);

            Assert.Equal("Shipped", await GetOrderStatus(client, orderId));
            Assert.Equal([(1, "Placed 2 x Teapot"), (2, "Shipped")], await shop.Factory.Services.GetOrderHistory(orderId));
            Assert.Equal(ShipmentStatus.Dispatched, (await shop.Factory.Services.FindShipment(orderId))?.Status);
        }

        [Fact]
        public async Task A_redelivered_dispatch_ships_the_order_once()
        {
            using HttpClient client = shop.Factory.CreateClient();
            Guid orderId = await PlacePreparedOrder(client);
            using HttpResponseMessage response = await client.DispatchShipment(orderId);
            await shop.Factory.Services.DrainOutbox(cancellationToken: Token);

            int redelivered = await shop.Factory.Services.Redeliver<ShipmentDispatchedIntegrationEvent>(
                e => e.OrderId == orderId, Token);
            await shop.Factory.Services.DrainOutbox(cancellationToken: Token);

            Assert.Equal(1, redelivered);
            Assert.Equal([(1, "Placed 2 x Teapot"), (2, "Shipped")], await shop.Factory.Services.GetOrderHistory(orderId));
        }

        [Fact]
        public async Task A_shipped_order_cannot_be_cancelled()
        {
            using HttpClient client = shop.Factory.CreateClient();
            Guid orderId = await PlacePreparedOrder(client);
            using HttpResponseMessage dispatched = await client.DispatchShipment(orderId);
            await shop.Factory.Services.DrainOutbox(cancellationToken: Token);

            using HttpResponseMessage response = await client.CancelOrder(orderId);

            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
            Microsoft.AspNetCore.Mvc.ProblemDetails? problem =
                await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>(Token);
            Assert.Equal($"Order {orderId} is shipped and can no longer be cancelled.", problem?.Detail);
            Assert.Equal("Shipped", await GetOrderStatus(client, orderId));
        }

        [Fact]
        public async Task An_order_cancelled_while_its_shipment_is_dispatched_gets_its_parcel_back()
        {
            using HttpClient client = shop.Factory.CreateClient();
            Guid orderId = await PlacePreparedOrder(client);

            // Each module accepts its change before it learns of the other's.
            using HttpResponseMessage cancelled = await client.CancelOrder(orderId);
            using HttpResponseMessage dispatched = await client.DispatchShipment(orderId);
            await shop.Factory.Services.DrainOutbox(cancellationToken: Token);

            Assert.Equal((HttpStatusCode.NoContent, HttpStatusCode.NoContent), (cancelled.StatusCode, dispatched.StatusCode));
            Assert.Equal("Cancelled", await GetOrderStatus(client, orderId));
            Assert.Equal([(1, "Placed 2 x Teapot"), (2, "Cancelled")], await shop.Factory.Services.GetOrderHistory(orderId));
            Shipment shipment = Assert.IsType<Shipment>(await shop.Factory.Services.FindShipment(orderId));
            Assert.Equal(ShipmentStatus.ReturnRequested, shipment.Status);
            Assert.Equal(
                [$"Order {orderId} was cancelled after dispatch: get the parcel back from the carrier."],
                await shop.Factory.Services.GetWarehouseNotices(shipment.Id));
        }

        [Fact]
        public async Task Dispatching_a_cancelled_shipment_answers_422()
        {
            using HttpClient client = shop.Factory.CreateClient();
            Guid orderId = await PlacePreparedOrder(client);
            using HttpResponseMessage cancelled = await client.CancelOrder(orderId);
            await shop.Factory.Services.DrainOutbox(cancellationToken: Token);

            using HttpResponseMessage response = await client.DispatchShipment(orderId);

            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
            Assert.Equal(ShipmentStatus.Cancelled, (await shop.Factory.Services.FindShipment(orderId))?.Status);
        }

        [Fact]
        public async Task Dispatching_an_order_without_a_shipment_answers_404()
        {
            using HttpClient client = shop.Factory.CreateClient();

            using HttpResponseMessage response = await client.DispatchShipment(Guid.CreateVersion7());

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        // An order whose shipment is being prepared: OrderPlacedIntegrationEvent delivered.
        private async Task<Guid> PlacePreparedOrder(HttpClient client)
        {
            Guid orderId = await client.PlaceOrder("Teapot", quantity: 2);
            await shop.Factory.Services.DrainOutbox(cancellationToken: Token);
            return orderId;
        }

        private static async Task<string?> GetOrderStatus(HttpClient client, Guid orderId) =>
            (await client.GetFromJsonAsync<ShopClient.Order>($"/orders/{orderId}", Token))?.Status;
    }
}
