using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Pheme.Outbox;

namespace Pheme.UnitTests
{
    public class OutboxMessageFactoryTests
    {
        private static readonly Guid EventId = Guid.Parse("4b4f2a5e-3f0c-4d8e-9a51-2f6c1d7e8b90");

        private readonly OutboxMessageFactory factory = new(new IntegrationEventTypes(new ServiceCollection()
            .AddTransient<IIntegrationEventHandler<OrderPlacedIntegrationEvent>, RecordingIntegrationEventHandler<OrderPlacedIntegrationEvent>>()
            .AddTransient<IIntegrationEventHandler<OrderCancelledIntegrationEvent>, RecordingIntegrationEventHandler<OrderCancelledIntegrationEvent>>()
            .AddTransient<IIntegrationEventHandler<LargePaymentReceivedIntegrationEvent>, RecordingIntegrationEventHandler<LargePaymentReceivedIntegrationEvent>>()));

        [Fact]
        public void Message_holds_the_event_under_its_name_with_the_time_in_utc()
        {
            var occurredOn = new DateTimeOffset(2026, 10, 10, 14, 30, 0, TimeSpan.FromHours(2));

            OutboxMessage message = factory.Create(new OrderPlacedIntegrationEvent(EventId, occurredOn, "A-1"), "Ordering");

            Assert.Equal(EventId, message.Id);
            Assert.Equal("tests.order-placed", message.Type);
            Assert.Equal(
                """{"eventId":"4b4f2a5e-3f0c-4d8e-9a51-2f6c1d7e8b90","occurredOn":"2026-10-10T14:30:00+02:00","orderNumber":"A-1"}""",
                message.Content);
            Assert.Equal("Ordering", message.Source);
            Assert.Equal(occurredOn, message.OccurredOnUtc);
            Assert.Equal(TimeSpan.Zero, message.OccurredOnUtc.Offset);
            Assert.Null(message.ProcessedOnUtc);
            Assert.Null(message.Error);
            Assert.Equal(0, message.AttemptCount);
            Assert.Null(message.CompletedHandlers);
        }

        [Fact]
        public void Message_holds_the_runtime_type_with_its_full_name_and_all_its_properties()
        {
            PaymentReceivedIntegrationEvent payment = new LargePaymentReceivedIntegrationEvent(EventId, DateTimeOffset.UnixEpoch, 1500m);

            OutboxMessage message = factory.Create(payment, "Billing");

            Assert.Equal("Pheme.UnitTests.LargePaymentReceivedIntegrationEvent", message.Type);
            Assert.Contains("\"amount\":1500", message.Content, StringComparison.Ordinal);
        }

        [Fact]
        public void Message_links_to_the_current_activity()
        {
            Assert.Null(factory.Create(new OrderCancelledIntegrationEvent(EventId, DateTimeOffset.UnixEpoch), "Ordering").TraceParent);

            using Activity activity = new Activity("PlaceOrder").SetIdFormat(ActivityIdFormat.W3C).Start();

            Assert.Equal(activity.Id, factory.Create(new OrderCancelledIntegrationEvent(EventId, DateTimeOffset.UnixEpoch), "Ordering").TraceParent);
            Assert.StartsWith("00-", activity.Id, StringComparison.Ordinal);
        }

        [Fact]
        public void Activity_without_a_w3c_id_is_not_linked()
        {
            using Activity activity = new Activity("PlaceOrder").SetIdFormat(ActivityIdFormat.Hierarchical).Start();

            Assert.Null(factory.Create(new OrderCancelledIntegrationEvent(EventId, DateTimeOffset.UnixEpoch), "Ordering").TraceParent);
        }

        [Fact]
        public void Event_type_without_a_valid_name_throws()
        {
            InvalidOperationException unnamed = Assert.Throws<InvalidOperationException>(
                () => factory.Create(new UnnamedIntegrationEvent(EventId, DateTimeOffset.UnixEpoch), "Ordering"));
            InvalidOperationException generic = Assert.Throws<InvalidOperationException>(
                () => factory.Create(new EnvelopeIntegrationEvent<int>(EventId, DateTimeOffset.UnixEpoch, 1), "Ordering"));

            Assert.StartsWith("Integration event 'Pheme.UnitTests.UnnamedIntegrationEvent' has an empty IntegrationEventName.", unnamed.Message, StringComparison.Ordinal);
            Assert.StartsWith("Integration event 'Pheme.UnitTests.EnvelopeIntegrationEvent<System.Int32>' is generic.", generic.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void Event_without_a_handler_of_its_runtime_type_throws()
        {
            InvalidOperationException subtype = Assert.Throws<InvalidOperationException>(
                () => factory.Create(new PaymentReceivedIntegrationEvent(EventId, DateTimeOffset.UnixEpoch), "Billing"));
            InvalidOperationException twin = Assert.Throws<InvalidOperationException>(
                () => factory.Create(new OrderPlacedTwinIntegrationEvent(EventId, DateTimeOffset.UnixEpoch), "Ordering"));

            Assert.Equal(
                "No handler of integration event 'Pheme.UnitTests.PaymentReceivedIntegrationEvent' is registered, so the outbox would park it. "
                + "Add a handler of the event, IIntegrationEventHandler<Pheme.UnitTests.PaymentReceivedIntegrationEvent>, in an assembly added "
                + "with AddHandlersFromAssemblies, or do not write it.",
                subtype.Message);
            Assert.StartsWith("No handler of integration event 'Pheme.UnitTests.OrderPlacedTwinIntegrationEvent' is registered", twin.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void Invalid_arguments_throw()
        {
            var integrationEvent = new OrderCancelledIntegrationEvent(EventId, DateTimeOffset.UnixEpoch);

            Assert.Throws<ArgumentNullException>("integrationEvent", () => factory.Create(null!, "Ordering"));
            Assert.Throws<ArgumentNullException>("source", () => factory.Create(integrationEvent, null!));
            Assert.Throws<ArgumentException>("source", () => factory.Create(integrationEvent, " "));
        }
    }
}
