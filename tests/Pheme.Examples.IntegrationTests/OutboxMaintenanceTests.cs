using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Ordering.Infrastructure;
using Shipping.Domain;
using Shop.Common.Contracts;

namespace Pheme.Examples.IntegrationTests
{
    /// <summary>
    /// Failed deliveries of the outbox, retried and parked, the administration endpoint requeueing a parked message,
    /// and the outbox health check. Each test runs its own instance of the application on a database of its own, so it
    /// counts the attempts of its own messages only.
    /// </summary>
    [Collection(typeof(ShopCollection))]
    public class OutboxMaintenanceTests(ShopFixture shop)
    {
        // OutboxOptions.MaxAttempts, unchanged by the configuration.
        private const int MaxAttempts = 10;

        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task A_handler_failing_twice_is_retried_until_it_succeeds()
        {
            await using WebApplicationFactory<Program> application = shop.CreateFactoryWithOwnDatabase();
            using HttpClient client = application.CreateClient();
            Guid orderId = await client.PlaceOrder();
            shop.Carrier.Fail(orderId, times: 2);

            // Each drain is one attempt here: the first failure throws, the second was reported already.
            await Assert.ThrowsAsync<InvalidOperationException>(() => application.Services.DrainOutbox(cancellationToken: Token));
            await application.Services.DrainOutbox(cancellationToken: Token);
            Assert.Null(await application.Services.FindShipment(orderId));

            await application.Services.DrainOutbox(cancellationToken: Token);

            Assert.Equal(ShipmentStatus.Preparing, (await application.Services.FindShipment(orderId))?.Status);
            Assert.Equal(3, shop.Carrier.PickupRequestsOf(orderId));
            Assert.Equal(1, shop.Notifier.ConfirmationsOf(orderId));
        }

        [Fact]
        public async Task A_failed_delivery_saves_its_outcome_but_not_the_shipment_its_handler_added()
        {
            await using WebApplicationFactory<Program> application = shop.CreateFactoryWithOwnDatabase();
            using HttpClient client = application.CreateClient();
            Guid failing = await client.PlaceOrder();
            Guid next = await client.PlaceOrder();
            shop.Carrier.Fail(failing, times: 1);

            await Assert.ThrowsAsync<InvalidOperationException>(() => application.Services.DrainOutbox(cancellationToken: Token));

            // A scope per handler: the next message's handler saved its own shipment, not the one the failed handler had
            // added before the carrier threw.
            Assert.Null(await application.Services.FindShipment(failing));
            Assert.NotNull(await application.Services.FindShipment(next));

            OutboxMessage message = await FindMessage(application.Services, failing);
            Assert.Null(message.ProcessedOnUtc);
            Assert.Equal(1, message.AttemptCount);
            Assert.Contains($"the pickup of order {failing} is not booked", message.Error, StringComparison.Ordinal);
            Assert.Contains("SendOrderConfirmationIntegrationEventHandler", message.CompletedHandlers, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_message_failing_every_attempt_is_parked_with_its_error_and_the_next_messages_are_still_delivered()
        {
            await using WebApplicationFactory<Program> application = shop.CreateFactoryWithOwnDatabase();
            using HttpClient client = application.CreateClient();
            Guid parked = await client.PlaceOrder();
            shop.Carrier.Fail(parked);

            await DeliverUntilParked(application.Services);

            ShopClient.ParkedMessage message = Assert.Single(await client.GetParkedOutboxMessages());
            Assert.Equal(("ordering.order-placed", "Ordering", MaxAttempts), (message.Type, message.Source, message.AttemptCount));
            Assert.Contains($"the pickup of order {parked} is not booked", message.Error, StringComparison.Ordinal);

            Guid next = await client.PlaceOrder();
            await application.Services.DrainOutbox(cancellationToken: Token);

            Assert.Equal(ShipmentStatus.Preparing, (await application.Services.FindShipment(next))?.Status);
            Assert.Null(await application.Services.FindShipment(parked));
            Assert.Equal(MaxAttempts, shop.Carrier.PickupRequestsOf(parked));
        }

        [Fact]
        public async Task A_parked_message_requeued_through_the_admin_endpoint_once_fixed_is_delivered_to_the_failed_handler_only()
        {
            await using WebApplicationFactory<Program> application = shop.CreateFactoryWithOwnDatabase();
            using HttpClient client = application.CreateClient();
            Guid orderId = await client.PlaceOrder();
            shop.Carrier.Fail(orderId);
            await DeliverUntilParked(application.Services);

            // The outbox check alerts while a message is parked; the probes stay healthy, as neither a restart nor taking
            // the instance out of traffic would deliver it.
            Assert.Equal((HttpStatusCode.ServiceUnavailable, "Unhealthy"), await client.GetHealth("/health/outbox"));
            Assert.Equal((HttpStatusCode.OK, "Healthy"), await client.GetHealth("/health"));

            shop.Carrier.Repair(orderId);
            ShopClient.ParkedMessage message = Assert.Single(await client.GetParkedOutboxMessages());
            using (HttpResponseMessage requeued = await client.RequeueOutboxMessage(message.Id))
            {
                Assert.Equal(HttpStatusCode.NoContent, requeued.StatusCode);
            }

            await application.Services.DrainOutbox(cancellationToken: Token);

            Assert.Equal(ShipmentStatus.Preparing, (await application.Services.FindShipment(orderId))?.Status);
            Assert.Equal(MaxAttempts + 1, shop.Carrier.PickupRequestsOf(orderId));
            Assert.Equal(1, shop.Notifier.ConfirmationsOf(orderId));
            Assert.Empty(await client.GetParkedOutboxMessages());
            Assert.Equal((HttpStatusCode.OK, "Healthy"), await client.GetHealth("/health/outbox"));

            using HttpResponseMessage processed = await client.RequeueOutboxMessage(message.Id);
            Assert.Equal(HttpStatusCode.NotFound, processed.StatusCode);
        }

        [Fact]
        public async Task The_outbox_check_is_degraded_while_a_message_waits_longer_than_five_minutes()
        {
            // The clock of the events and of the health check, in whole microseconds as PostgreSQL stores them: a finer
            // start could be stored earlier, and the message would wait longer than five minutes after five minutes.
            DateTimeOffset now = DateTimeOffset.UtcNow;
            var clock = new FakeTimeProvider(now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMicrosecond)));
            await using WebApplicationFactory<Program> application = shop.CreateFactoryWithOwnDatabase().WithWebHostBuilder(builder =>
                builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(clock)));
            using HttpClient client = application.CreateClient();
            await client.PlaceOrder();

            clock.Advance(TimeSpan.FromMinutes(5));
            Assert.Equal((HttpStatusCode.OK, "Healthy"), await client.GetHealth("/health/outbox"));

            clock.Advance(TimeSpan.FromSeconds(1));
            Assert.Equal((HttpStatusCode.OK, "Degraded"), await client.GetHealth("/health/outbox"));

            await application.Services.DrainOutbox(cancellationToken: Token);
            Assert.Equal((HttpStatusCode.OK, "Healthy"), await client.GetHealth("/health/outbox"));
        }

        // The first attempt throws; the next ones fail silently, as reported already, and the last one parks the message.
        private static async Task DeliverUntilParked(IServiceProvider services)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => services.DrainOutbox(cancellationToken: Token));
            for (int attempt = 2; attempt <= MaxAttempts; attempt++)
            {
                await services.DrainOutbox(cancellationToken: Token);
            }
        }

        // The message of the order's integration event, as the outbox table holds it.
        private static async Task<OutboxMessage> FindMessage(IServiceProvider services, Guid orderId)
        {
            OrderPlacedIntegrationEvent placed = Assert.Single(
                await services.GetOutboxEvents<OrderPlacedIntegrationEvent>(cancellationToken: Token),
                e => e.OrderId == orderId);
            await using AsyncServiceScope scope = services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<OrderingDbContext>().Set<OutboxMessage>()
                .AsNoTracking()
                .SingleAsync(m => m.Id == placed.EventId, Token);
        }
    }
}
