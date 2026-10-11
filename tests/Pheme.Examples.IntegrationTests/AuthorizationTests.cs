using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Shipping.Domain;

namespace Pheme.Examples.IntegrationTests
{
    /// <summary>
    /// The application's authorization behavior: a message requiring a role (<c>IRequireRole</c>) is refused with 403
    /// for any other user, before it is validated or handled.
    /// </summary>
    [Collection(typeof(ShopCollection))]
    public class AuthorizationTests(ShopFixture shop)
    {
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task Dispatching_a_shipment_outside_the_warehouse_staff_answers_403_and_dispatches_nothing()
        {
            using HttpClient client = shop.Factory.CreateClient();
            Guid orderId = await client.PlaceOrder();
            await shop.Factory.Services.DrainOutbox(cancellationToken: Token);

            using HttpResponseMessage anonymous = await client.DispatchShipment(orderId, roles: null);
            using HttpResponseMessage sales = await client.DispatchShipment(orderId, roles: "sales");

            Assert.Equal(HttpStatusCode.Forbidden, anonymous.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, sales.StatusCode);
            Assert.Equal(ShipmentStatus.Preparing, (await shop.Factory.Services.FindShipment(orderId))?.Status);

            using HttpResponseMessage warehouse = await client.DispatchShipment(orderId, roles: "sales, warehouse");

            Assert.Equal(HttpStatusCode.NoContent, warehouse.StatusCode);
            Assert.Equal(ShipmentStatus.Dispatched, (await shop.Factory.Services.FindShipment(orderId))?.Status);
        }

        [Fact]
        public async Task A_message_outside_the_users_roles_is_refused_before_it_is_validated()
        {
            using HttpClient client = shop.Factory.CreateClient();

            using HttpResponseMessage refused = await client.ReceiveStock("Teapot", quantity: 0, roles: "sales");
            using HttpResponseMessage invalid = await client.ReceiveStock("Teapot", quantity: 0);

            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
    }
}
