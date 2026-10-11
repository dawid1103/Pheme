using Microsoft.Extensions.DependencyInjection;
using Pheme.Outbox;
using Pheme.TestFixtures;

namespace Pheme.UnitTests
{
    public class IntegrationEventDispatcherTests
    {
        private const string Recording = "Pheme.UnitTests.RecordingIntegrationEventHandler<Pheme.UnitTests.OrderPlacedIntegrationEvent>";
        private const string SecondRecording = "Pheme.UnitTests.SecondRecordingIntegrationEventHandler<Pheme.UnitTests.OrderPlacedIntegrationEvent>";
        private const string Failing = "Pheme.UnitTests.FailingIntegrationEventHandler<Pheme.UnitTests.OrderPlacedIntegrationEvent>";

        private static readonly OrderPlacedIntegrationEvent Placed = new(Guid.NewGuid(), DateTimeOffset.UnixEpoch, "A-1");

        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task Handler_receives_the_event_and_is_returned_as_completed()
        {
            using ServiceProvider provider = Build(services => services
                .AddTransient<IIntegrationEventHandler<OrderPlacedIntegrationEvent>, RecordingIntegrationEventHandler<OrderPlacedIntegrationEvent>>());

            IntegrationEventDispatchResult result = await Dispatch(provider, Placed);

            Assert.Null(result.Error);
            Assert.Equal([Recording], result.CompletedHandlers);
            Assert.Equal([("RecordingIntegrationEventHandler`1", (IIntegrationEvent)Placed)], provider.GetRequiredService<OutboxLog>().Deliveries);
        }

        [Fact]
        public async Task Handlers_run_in_registration_order_and_the_same_class_once()
        {
            using ServiceProvider provider = Build(services => services
                .AddTransient<IIntegrationEventHandler<OrderPlacedIntegrationEvent>, SecondRecordingIntegrationEventHandler<OrderPlacedIntegrationEvent>>()
                .AddTransient<IIntegrationEventHandler<OrderPlacedIntegrationEvent>, RecordingIntegrationEventHandler<OrderPlacedIntegrationEvent>>()
                .AddTransient<IIntegrationEventHandler<OrderPlacedIntegrationEvent>, SecondRecordingIntegrationEventHandler<OrderPlacedIntegrationEvent>>());

            IntegrationEventDispatchResult result = await Dispatch(provider, Placed);

            Assert.Null(result.Error);
            Assert.Equal([SecondRecording, Recording], result.CompletedHandlers);
            Assert.Equal(["SecondRecordingIntegrationEventHandler`1", "RecordingIntegrationEventHandler`1"], Handlers(provider));
        }

        [Fact]
        public async Task Failed_handler_does_not_stop_the_others_and_is_not_completed()
        {
            using ServiceProvider provider = Build(services => services
                .AddTransient<IIntegrationEventHandler<OrderPlacedIntegrationEvent>, FailingIntegrationEventHandler<OrderPlacedIntegrationEvent>>()
                .AddTransient<IIntegrationEventHandler<OrderPlacedIntegrationEvent>, RecordingIntegrationEventHandler<OrderPlacedIntegrationEvent>>());

            IntegrationEventDispatchResult result = await Dispatch(provider, Placed);

            AggregateException error = Assert.IsType<AggregateException>(result.Error);
            Assert.Equal("The handler failed.", Assert.Single(error.InnerExceptions).Message);
            Assert.Equal([Recording], result.CompletedHandlers);
            Assert.Equal(["FailingIntegrationEventHandler`1", "RecordingIntegrationEventHandler`1"], Handlers(provider));
        }

        [Fact]
        public async Task Completed_handlers_of_earlier_attempts_are_skipped_and_kept()
        {
            using ServiceProvider provider = Build(services => services
                .AddTransient<IIntegrationEventHandler<OrderPlacedIntegrationEvent>, RecordingIntegrationEventHandler<OrderPlacedIntegrationEvent>>()
                .AddTransient<IIntegrationEventHandler<OrderPlacedIntegrationEvent>, SecondRecordingIntegrationEventHandler<OrderPlacedIntegrationEvent>>());

            IntegrationEventDispatchResult result = await Dispatch(provider, Placed, [Recording, "Removed.Handler"]);

            Assert.Null(result.Error);
            Assert.Equal([Recording, "Removed.Handler", SecondRecording], result.CompletedHandlers);
            Assert.Equal(["SecondRecordingIntegrationEventHandler`1"], Handlers(provider));
        }

        [Fact]
        public async Task Class_handling_several_events_receives_each_of_them()
        {
            var cancelled = new OrderCancelledIntegrationEvent(Guid.NewGuid(), DateTimeOffset.UnixEpoch);
            using ServiceProvider provider = Build(services => services
                .AddTransient<IIntegrationEventHandler<OrderPlacedIntegrationEvent>, OrderIntegrationEventHandler>()
                .AddTransient<IIntegrationEventHandler<OrderCancelledIntegrationEvent>, OrderIntegrationEventHandler>());

            IntegrationEventDispatchResult placedResult = await Dispatch(provider, Placed);
            IntegrationEventDispatchResult cancelledResult = await Dispatch(provider, cancelled);

            Assert.Equal(["Pheme.UnitTests.OrderIntegrationEventHandler"], placedResult.CompletedHandlers);
            Assert.Equal(["Pheme.UnitTests.OrderIntegrationEventHandler"], cancelledResult.CompletedHandlers);
            Assert.Equal(
                [("OrderIntegrationEventHandler", (IIntegrationEvent)Placed), ("OrderIntegrationEventHandler", cancelled)],
                provider.GetRequiredService<OutboxLog>().Deliveries);
        }

        [Fact]
        public async Task Nested_handler_is_recorded_as_completed_under_its_full_name_with_a_plus()
        {
            using ServiceProvider provider = Build(services => services
                .AddTransient<IIntegrationEventHandler<OrderPlacedIntegrationEvent>, Nested.RecordingIntegrationEventHandler<OrderPlacedIntegrationEvent>>());

            IntegrationEventDispatchResult result = await Dispatch(provider, Placed);

            Assert.Equal(["Pheme.UnitTests.IntegrationEventDispatcherTests+Nested+RecordingIntegrationEventHandler<Pheme.UnitTests.OrderPlacedIntegrationEvent>"], result.CompletedHandlers);
        }

        [Fact]
        public async Task Unknown_type_is_an_error_of_the_message()
        {
            using ServiceProvider provider = Build(services => services
                .AddTransient<IIntegrationEventHandler<OrderPlacedIntegrationEvent>, RecordingIntegrationEventHandler<OrderPlacedIntegrationEvent>>());

            IntegrationEventDispatchResult result = await Dispatch(provider, "Pheme.UnitTests.OrderPlacedIntegrationEvent", "{}", [Recording]);

            InvalidOperationException error = Assert.IsType<InvalidOperationException>(result.Error);
            Assert.StartsWith("No integration event handled by this application is named 'Pheme.UnitTests.OrderPlacedIntegrationEvent'.", error.Message, StringComparison.Ordinal);
            Assert.Equal([Recording], result.CompletedHandlers);
            Assert.Empty(Handlers(provider));
        }

        [Theory]
        [InlineData("null")]
        [InlineData("{\"eventId\":")]
        public async Task Content_that_does_not_deserialize_is_an_error_of_the_message(string content)
        {
            using ServiceProvider provider = Build(services => services
                .AddTransient<IIntegrationEventHandler<OrderPlacedIntegrationEvent>, RecordingIntegrationEventHandler<OrderPlacedIntegrationEvent>>());

            IntegrationEventDispatchResult result = await Dispatch(provider, "tests.order-placed", content, []);

            Assert.NotNull(result.Error);
            Assert.Empty(result.CompletedHandlers);
            Assert.Empty(Handlers(provider));
        }

        [Fact]
        public async Task Content_with_pascal_case_properties_deserializes()
        {
            using ServiceProvider provider = Build(services => services
                .AddTransient<IIntegrationEventHandler<OrderPlacedIntegrationEvent>, RecordingIntegrationEventHandler<OrderPlacedIntegrationEvent>>());
            string content = $$"""{"EventId":"{{Placed.EventId}}","OccurredOn":"1970-01-01T00:00:00+00:00","OrderNumber":"A-1"}""";

            IntegrationEventDispatchResult result = await Dispatch(provider, "tests.order-placed", content, []);

            Assert.Null(result.Error);
            Assert.Equal(Placed, Assert.Single(provider.GetRequiredService<OutboxLog>().Deliveries).Event);
        }

        [Fact]
        public async Task Handler_that_cannot_be_resolved_is_an_error_of_the_message()
        {
            using ServiceProvider provider = new ServiceCollection()
                .AddPheme(options => options.AddHandlersFromAssemblyContaining<Ping>().AddOutbox())
                .AddInMemoryOutboxStore()
                .AddTransient<IIntegrationEventHandler<OrderPlacedIntegrationEvent>, OrderIntegrationEventHandler>()
                .BuildServiceProvider();

            IntegrationEventDispatchResult result = await Dispatch(provider, Placed);

            Assert.IsType<InvalidOperationException>(result.Error);
            Assert.Empty(result.CompletedHandlers);
        }

        [Fact]
        public async Task Cancelled_delivery_stops_after_the_failed_handler()
        {
            using ServiceProvider provider = Build(services => services
                .AddTransient<IIntegrationEventHandler<OrderPlacedIntegrationEvent>, RecordingIntegrationEventHandler<OrderPlacedIntegrationEvent>>()
                .AddTransient<IIntegrationEventHandler<OrderPlacedIntegrationEvent>, CancelledIntegrationEventHandler<OrderPlacedIntegrationEvent>>()
                .AddTransient<IIntegrationEventHandler<OrderPlacedIntegrationEvent>, SecondRecordingIntegrationEventHandler<OrderPlacedIntegrationEvent>>());
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync();

            IntegrationEventDispatchResult result = await Dispatch(provider, Placed, [], cancellation.Token);

            AggregateException error = Assert.IsType<AggregateException>(result.Error);
            Assert.IsType<OperationCanceledException>(Assert.Single(error.InnerExceptions));
            Assert.Equal([Recording], result.CompletedHandlers);
            Assert.Equal(["RecordingIntegrationEventHandler`1"], Handlers(provider));
        }

        private static ServiceProvider Build(Action<IServiceCollection> handlers)
        {
            IServiceCollection services = new ServiceCollection()
                .AddPheme(options => options.AddHandlersFromAssemblyContaining<Ping>().AddOutbox())
                .AddInMemoryOutboxStore()
                .AddSingleton<OutboxLog>();
            handlers(services);
            return services.BuildServiceProvider(validateScopes: true);
        }

        private static Task<IntegrationEventDispatchResult> Dispatch(
            ServiceProvider provider, IIntegrationEvent integrationEvent, IReadOnlyCollection<string>? completedHandlers = null, CancellationToken? cancellationToken = null)
        {
            OutboxMessage message = provider.GetRequiredService<IOutboxMessageFactory>().Create(integrationEvent, "Tests");
            return Dispatch(provider, message.Type, message.Content, completedHandlers ?? [], cancellationToken);
        }

        private static async Task<IntegrationEventDispatchResult> Dispatch(
            ServiceProvider provider, string type, string content, IReadOnlyCollection<string> completedHandlers, CancellationToken? cancellationToken = null)
        {
            return await provider.GetRequiredService<IntegrationEventDispatcher>()
                .Dispatch(type, content, completedHandlers, cancellationToken ?? Token);
        }

        private static IEnumerable<string> Handlers(ServiceProvider provider)
        {
            return provider.GetRequiredService<OutboxLog>().Deliveries.Select(delivery => delivery.Handler);
        }

        // Generic, so the scan of the test assembly skips it.
        private static class Nested
        {
            public sealed class RecordingIntegrationEventHandler<TEvent>(OutboxLog log) : IIntegrationEventHandler<TEvent>
                where TEvent : IIntegrationEvent
            {
                public Task Handle(TEvent integrationEvent, CancellationToken cancellationToken)
                {
                    log.Add(this, integrationEvent);
                    return Task.CompletedTask;
                }
            }
        }
    }
}
