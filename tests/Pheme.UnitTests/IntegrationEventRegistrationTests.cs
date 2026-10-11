using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pheme.Outbox;
using Pheme.Registration;
using Pheme.TestFixtures;

namespace Pheme.UnitTests
{
    public class IntegrationEventRegistrationTests
    {
        [Fact]
        public void Scan_registers_integration_event_handlers_as_transient_one_descriptor_per_event()
        {
            IServiceCollection services = new ServiceCollection()
                .AddPheme(options => options.AddHandlersFromAssemblyContaining<IntegrationEventRegistrationTests>());

            Assert.Equal(
                [typeof(IIntegrationEventHandler<OrderCancelledIntegrationEvent>), typeof(IIntegrationEventHandler<OrderPlacedIntegrationEvent>)],
                services.Where(descriptor => descriptor.ImplementationType == typeof(OrderIntegrationEventHandler))
                    .Select(descriptor => descriptor.ServiceType)
                    .OrderBy(type => type.GetGenericArguments()[0].Name, StringComparer.Ordinal));
            Assert.All(
                services.Where(descriptor => descriptor.ImplementationType == typeof(OrderIntegrationEventHandler)),
                descriptor => Assert.Equal(ServiceLifetime.Transient, descriptor.Lifetime));
        }

        [Fact]
        public void Handlers_without_the_outbox_fail_on_start()
        {
            using ServiceProvider provider = new ServiceCollection()
                .AddPheme(options => options.AddHandlersFromAssemblyContaining<Ping>())
                .AddTransient<IIntegrationEventHandler<OrderPlacedIntegrationEvent>, RecordingIntegrationEventHandler<OrderPlacedIntegrationEvent>>()
                .AddTransient<IIntegrationEventHandler<OrderPlacedIntegrationEvent>>(_ => null!)
                .BuildServiceProvider();

            OptionsValidationException exception = Assert.Throws<OptionsValidationException>(
                () => provider.GetRequiredService<IStartupValidator>().Validate());

            Assert.Equal(
                "Pheme handler registrations have 1 problem:" + Environment.NewLine
                + "- Integration event handlers are registered ('Pheme.UnitTests.RecordingIntegrationEventHandler<Pheme.UnitTests.OrderPlacedIntegrationEvent>', "
                + "a factory delegate), but the outbox is not, so nothing would deliver their events. Add it with "
                + "PhemeOptions.AddOutbox(); integration events are delivered only through the outbox, in-process events "
                + "are domain events.",
                exception.Message);
        }

        [Fact]
        public void Scan_collects_the_concrete_integration_event_types()
        {
            IServiceCollection services = new ServiceCollection()
                .AddPheme(options => options.AddHandlersFromAssemblyContaining<IntegrationEventRegistrationTests>());

            IReadOnlyList<Type> eventTypes = HandlerRegistry.GetOrAdd(services).IntegrationEventTypes;
            Assert.Contains(typeof(OrderPlacedIntegrationEvent), eventTypes);
            Assert.Contains(typeof(LargePaymentReceivedIntegrationEvent), eventTypes);
            Assert.DoesNotContain(typeof(AbstractIntegrationEvent), eventTypes);
            Assert.DoesNotContain(typeof(EnvelopeIntegrationEvent<>), eventTypes);
        }

        [Fact]
        public void Integration_events_of_the_scanned_assemblies_without_a_handler_fail_on_start_with_the_outbox()
        {
            using ServiceProvider provider = new ServiceCollection()
                .AddPheme(options => options.AddHandlersFromAssemblyContaining<IntegrationEventRegistrationTests>().AddOutbox())
                .AddInMemoryOutboxStore()
                .BuildServiceProvider();

            OptionsValidationException exception = Assert.Throws<OptionsValidationException>(
                () => provider.GetRequiredService<IStartupValidator>().Validate());

            Assert.Contains(
                "- Integration event 'Pheme.UnitTests.PaymentReceivedIntegrationEvent' has no handler, so the outbox would "
                + "park it. Implement 'IIntegrationEventHandler<Pheme.UnitTests.PaymentReceivedIntegrationEvent>'. Pheme "
                + "searched assemblies 'Pheme.UnitTests'; is the handler's assembly added with AddHandlersFromAssemblies?",
                exception.Message,
                StringComparison.Ordinal);
            Assert.DoesNotContain("'Pheme.UnitTests.OrderPlacedIntegrationEvent' has no handler", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void Keyed_handlers_do_not_count_as_the_handler_of_an_integration_event()
        {
            IServiceCollection services = WithOutbox()
                .AddTransient<IIntegrationEventHandler<OrderPlacedIntegrationEvent>, RecordingIntegrationEventHandler<OrderPlacedIntegrationEvent>>()
                .AddKeyedTransient<IIntegrationEventHandler<OrderCancelledIntegrationEvent>, RecordingIntegrationEventHandler<OrderCancelledIntegrationEvent>>("keyed");

            string problem = Assert.Single(IntegrationEventTypes.FindProblems(
                services, [typeof(OrderPlacedIntegrationEvent), typeof(OrderCancelledIntegrationEvent)], [typeof(Ping).Assembly]));

            Assert.StartsWith("Integration event 'Pheme.UnitTests.OrderCancelledIntegrationEvent' has no handler", problem, StringComparison.Ordinal);
        }

        [Fact]
        public void Integration_events_need_no_handler_without_the_outbox()
        {
            Assert.Empty(IntegrationEventTypes.FindProblems(new ServiceCollection(), [typeof(OrderPlacedIntegrationEvent)], []));
        }

        [Fact]
        public void Keyed_handlers_are_never_called_so_they_need_no_outbox()
        {
            IServiceCollection services = new ServiceCollection()
                .AddKeyedTransient<IIntegrationEventHandler<OrderPlacedIntegrationEvent>, RecordingIntegrationEventHandler<OrderPlacedIntegrationEvent>>("keyed");

            Assert.Empty(IntegrationEventTypes.FindProblems(services, [], []));
        }

        [Fact]
        public void Valid_handlers_with_the_outbox_pass_on_start_and_map_stored_names_to_event_types()
        {
            using ServiceProvider provider = WithOutbox()
                .AddTransient<IIntegrationEventHandler<OrderPlacedIntegrationEvent>, OrderIntegrationEventHandler>()
                .AddTransient<IIntegrationEventHandler<OrderCancelledIntegrationEvent>, OrderIntegrationEventHandler>()
                .AddTransient<IIntegrationEventHandler<PaymentReceivedIntegrationEvent>>(provider => new RecordingIntegrationEventHandler<PaymentReceivedIntegrationEvent>(provider.GetRequiredService<OutboxLog>()))
                .BuildServiceProvider();

            provider.GetRequiredService<IStartupValidator>().Validate();

            var types = provider.GetRequiredService<IntegrationEventTypes>();
            Assert.Equal(typeof(OrderPlacedIntegrationEvent), types.Find("tests.order-placed"));
            Assert.Equal(typeof(OrderCancelledIntegrationEvent), types.Find("Pheme.UnitTests.OrderCancelledIntegrationEvent"));
            Assert.Equal(typeof(PaymentReceivedIntegrationEvent), types.Find("Pheme.UnitTests.PaymentReceivedIntegrationEvent"));
            Assert.Null(types.Find("Pheme.UnitTests.OrderPlacedIntegrationEvent"));
            Assert.Null(types.Find("Pheme.UnitTests.LargePaymentReceivedIntegrationEvent"));
        }

        [Fact]
        public void Event_types_with_an_empty_name_or_a_generic_type_are_reported()
        {
            IServiceCollection services = WithOutbox()
                .AddTransient<IIntegrationEventHandler<UnnamedIntegrationEvent>, RecordingIntegrationEventHandler<UnnamedIntegrationEvent>>()
                .AddTransient<IIntegrationEventHandler<EnvelopeIntegrationEvent<OrderPlacedIntegrationEvent>>, RecordingIntegrationEventHandler<EnvelopeIntegrationEvent<OrderPlacedIntegrationEvent>>>();

            Assert.Equal(
                [
                    "Integration event 'Pheme.UnitTests.UnnamedIntegrationEvent' has an empty IntegrationEventName. Give it a stable name, "
                    + "for example [IntegrationEventName(\"ordering.order-placed\")], or remove the attribute to use the "
                    + "full type name.",
                    "Integration event 'Pheme.UnitTests.EnvelopeIntegrationEvent<Pheme.UnitTests.OrderPlacedIntegrationEvent>' is generic. The outbox stores an event "
                    + "under the name of its type, which for a generic type depends on its type arguments; use a non-generic type.",
                ],
                IntegrationEventTypes.FindProblems(services, [], []));
        }

        [Fact]
        public void Event_types_with_the_same_name_are_reported_once_with_all_of_them()
        {
            IServiceCollection services = WithOutbox()
                .AddTransient<IIntegrationEventHandler<OrderPlacedIntegrationEvent>, RecordingIntegrationEventHandler<OrderPlacedIntegrationEvent>>()
                .AddTransient<IIntegrationEventHandler<OrderPlacedIntegrationEvent>, SecondRecordingIntegrationEventHandler<OrderPlacedIntegrationEvent>>()
                .AddTransient<IIntegrationEventHandler<OrderPlacedTwinIntegrationEvent>, RecordingIntegrationEventHandler<OrderPlacedTwinIntegrationEvent>>();

            Assert.Equal(
                [
                    "Integration events 'Pheme.UnitTests.OrderPlacedIntegrationEvent', 'Pheme.UnitTests.OrderPlacedTwinIntegrationEvent' have the same name "
                    + "'tests.order-placed', which identifies the event type of a stored message. Give each event a unique "
                    + "name with IntegrationEventName.",
                ],
                IntegrationEventTypes.FindProblems(services, [], []));
        }

        [Fact]
        public void Handlers_of_an_interface_or_abstract_event_type_are_reported()
        {
            IServiceCollection services = WithOutbox()
                .AddTransient<IIntegrationEventHandler<IIntegrationEvent>, RecordingIntegrationEventHandler<IIntegrationEvent>>()
                .AddTransient<IIntegrationEventHandler<AbstractIntegrationEvent>, RecordingIntegrationEventHandler<AbstractIntegrationEvent>>()
                .AddTransient<IIntegrationEventHandler<AbstractIntegrationEvent>, SecondRecordingIntegrationEventHandler<AbstractIntegrationEvent>>();

            Assert.Equal(
                [
                    "Integration event 'Pheme.IIntegrationEvent' is an interface, so its handlers "
                    + "'Pheme.UnitTests.RecordingIntegrationEventHandler<Pheme.IIntegrationEvent>' never run: the outbox delivers an event only to "
                    + "the handlers of its exact runtime type. Handle the concrete event types.",
                    "Integration event 'Pheme.UnitTests.AbstractIntegrationEvent' is abstract, so its handlers "
                    + "'Pheme.UnitTests.RecordingIntegrationEventHandler<Pheme.UnitTests.AbstractIntegrationEvent>', 'Pheme.UnitTests.SecondRecordingIntegrationEventHandler<Pheme.UnitTests.AbstractIntegrationEvent>' "
                    + "never run: the outbox delivers an event only to the handlers of its exact runtime type. Handle the "
                    + "concrete event types.",
                ],
                IntegrationEventTypes.FindProblems(services, [], []));
        }

        [Fact]
        public void Dispatcher_validates_the_registrations_when_no_host_has()
        {
            using ServiceProvider provider = WithOutbox()
                .AddTransient<IIntegrationEventHandler<UnnamedIntegrationEvent>, RecordingIntegrationEventHandler<UnnamedIntegrationEvent>>()
                .BuildServiceProvider();

            OptionsValidationException exception = Assert.Throws<OptionsValidationException>(
                () => provider.GetRequiredService<IntegrationEventDispatcher>());

            Assert.Contains("'Pheme.UnitTests.UnnamedIntegrationEvent' has an empty IntegrationEventName.", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void Several_calls_add_the_outbox_once_and_configure_it_in_call_order()
        {
            IServiceCollection services = new ServiceCollection()
                .AddPheme(options => options
                    .AddOutbox(outbox => outbox.BatchSize = 50)
                    .AddOutbox(outbox => outbox.MaxAttempts = 3))
                .AddPheme(options => options
                    .AddOutbox()
                    .AddOutbox(outbox => outbox.BatchSize = 10));
            using ServiceProvider provider = services.BuildServiceProvider();

            OutboxOptions options = provider.GetRequiredService<IOptions<OutboxOptions>>().Value;
            Assert.Equal(10, options.BatchSize);
            Assert.Equal(3, options.MaxAttempts);
            Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IntegrationEventDispatcher));
            Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IOutboxMessageFactory));
            Assert.IsType<OutboxMessageFactory>(provider.GetRequiredService<IOutboxMessageFactory>());
        }

        [Fact]
        public void AddOutbox_with_null_configuration_throws()
        {
            Assert.Throws<ArgumentNullException>(
                "configure", () => new ServiceCollection().AddPheme(options => options.AddOutbox(null!)));
        }

        private static IServiceCollection WithOutbox()
        {
            return new ServiceCollection()
                .AddPheme(options => options.AddHandlersFromAssemblyContaining<Ping>().AddOutbox())
                .AddInMemoryOutboxStore()
                .AddSingleton<OutboxLog>();
        }
    }
}
