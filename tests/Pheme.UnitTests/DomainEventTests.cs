using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pheme.TestFixtures;

namespace Pheme.UnitTests
{
    public class DomainEventTests
    {
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task Event_reaches_only_the_handlers_of_its_type()
        {
            using ServiceProvider provider = BuildFixturesProvider();
            using IServiceScope scope = provider.CreateScope();

            await scope.ServiceProvider.GetRequiredService<IDomainEventPublisher>().Publish(new EntryAddedDomainEvent("first"), Token);

            Assert.Equal(["audit: first", "journal: first"], provider.GetRequiredService<Journal>().Entries);
        }

        [Fact]
        public async Task Class_handling_several_events_handles_each_of_them()
        {
            using ServiceProvider provider = BuildFixturesProvider();
            using IServiceScope scope = provider.CreateScope();

            await scope.ServiceProvider.GetRequiredService<IDomainEventPublisher>().Publish([new JournalClearedDomainEvent(), new EntryAddedDomainEvent("next")], Token);

            Assert.Equal(["journal: cleared", "audit: next", "journal: next"], provider.GetRequiredService<Journal>().Entries);
        }

        [Fact]
        public async Task Handler_of_a_base_type_does_not_receive_a_derived_event()
        {
            using ServiceProvider provider = BuildFixturesProvider();
            using IServiceScope scope = provider.CreateScope();
            IDomainEventPublisher publisher = scope.ServiceProvider.GetRequiredService<IDomainEventPublisher>();

            await publisher.Publish(new UrgentNoteTakenDomainEvent("urgent"), Token);
            await publisher.Publish(new NoteTakenDomainEvent("plain"), Token);

            Assert.Equal(["note: plain"], provider.GetRequiredService<Journal>().Entries);
        }

        [Fact]
        public void Scan_registers_domain_event_handlers_in_the_order_of_their_full_type_names()
        {
            IServiceCollection services = new ServiceCollection()
                .AddPheme(options => options.AddHandlersFromAssemblyContaining<EntryAddedDomainEvent>());

            Assert.Equal(
                ["Pheme.TestFixtures.AuditEntryDomainEventHandler", "Pheme.TestFixtures.JournalDomainEventHandler"],
                services.Where(descriptor => descriptor.ServiceType == typeof(IDomainEventHandler<EntryAddedDomainEvent>))
                    .Select(descriptor => descriptor.ImplementationType!.FullName));
        }

        [Fact]
        public void Scan_registers_domain_event_handlers_as_transient()
        {
            IServiceCollection services = new ServiceCollection()
                .AddPheme(options => options.AddHandlersFromAssemblyContaining<EntryAddedDomainEvent>());

            ServiceDescriptor[] descriptors = [.. services.Where(descriptor =>
                descriptor.ServiceType.IsGenericType && descriptor.ServiceType.GetGenericTypeDefinition() == typeof(IDomainEventHandler<>))];
            Assert.Equal(4, descriptors.Length);
            Assert.All(descriptors, descriptor => Assert.Equal(ServiceLifetime.Transient, descriptor.Lifetime));
        }

        [Fact]
        public async Task Handlers_run_in_registration_order()
        {
            using ServiceProvider provider = BuildProvider(services => services
                .AddTransient<IDomainEventHandler<ShippedDomainEvent>, SecondShippedDomainEventHandler>()
                .AddTransient<IDomainEventHandler<ShippedDomainEvent>, FirstShippedDomainEventHandler>());
            using IServiceScope scope = provider.CreateScope();

            await scope.ServiceProvider.GetRequiredService<IDomainEventPublisher>().Publish(new ShippedDomainEvent(1), Token);

            Assert.Equal(["second 1", "first 1"], provider.GetRequiredService<EventLog>().Entries);
        }

        [Fact]
        public async Task Events_are_published_in_order_each_to_all_its_handlers_first()
        {
            using ServiceProvider provider = BuildProvider(services => services
                .AddTransient<IDomainEventHandler<ShippedDomainEvent>, FirstShippedDomainEventHandler>()
                .AddTransient<IDomainEventHandler<ShippedDomainEvent>, SecondShippedDomainEventHandler>()
                .AddTransient<IDomainEventHandler<DeliveredDomainEvent>, DeliveredDomainEventHandler>());
            using IServiceScope scope = provider.CreateScope();

            await scope.ServiceProvider.GetRequiredService<IDomainEventPublisher>()
                .Publish([new ShippedDomainEvent(1), new DeliveredDomainEvent(1), new ShippedDomainEvent(2)], Token);

            Assert.Equal(["first 1", "second 1", "delivered 1", "first 2", "second 2"], provider.GetRequiredService<EventLog>().Entries);
        }

        [Fact]
        public async Task Each_handler_is_awaited_before_the_next_one_starts()
        {
            using ServiceProvider provider = BuildProvider(services => services
                .AddTransient<IDomainEventHandler<ShippedDomainEvent>, SlowShippedDomainEventHandler>()
                .AddTransient<IDomainEventHandler<ShippedDomainEvent>, FirstShippedDomainEventHandler>());
            using IServiceScope scope = provider.CreateScope();

            await scope.ServiceProvider.GetRequiredService<IDomainEventPublisher>().Publish(new ShippedDomainEvent(1), Token);

            Assert.Equal(["slow started 1", "slow finished 1", "first 1"], provider.GetRequiredService<EventLog>().Entries);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task First_exception_stops_the_remaining_handlers_and_events_and_reaches_the_caller_unchanged(bool throwAfterAwait)
        {
            var failure = new InvalidOperationException("Shipping failed.");
            using ServiceProvider provider = BuildProvider(services => services
                .AddSingleton(new ShippingFailure(failure, throwAfterAwait))
                .AddTransient<IDomainEventHandler<ShippedDomainEvent>, FirstShippedDomainEventHandler>()
                .AddTransient<IDomainEventHandler<ShippedDomainEvent>, FailingShippedDomainEventHandler>()
                .AddTransient<IDomainEventHandler<ShippedDomainEvent>, SecondShippedDomainEventHandler>()
                .AddTransient<IDomainEventHandler<DeliveredDomainEvent>, DeliveredDomainEventHandler>());
            using IServiceScope scope = provider.CreateScope();

            // A synchronous throw lands in the returned task too, like an exception after an await.
            Task publication = scope.ServiceProvider.GetRequiredService<IDomainEventPublisher>()
                .Publish([new ShippedDomainEvent(1), new DeliveredDomainEvent(1)], Token);
            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() => publication);

            Assert.Same(failure, exception);
            Assert.Equal(["first 1"], provider.GetRequiredService<EventLog>().Entries);
        }

        [Fact]
        public async Task Event_without_handlers_and_empty_list_are_no_ops()
        {
            using ServiceProvider provider = BuildProvider(_ => { });
            using IServiceScope scope = provider.CreateScope();
            IDomainEventPublisher publisher = scope.ServiceProvider.GetRequiredService<IDomainEventPublisher>();

            await publisher.Publish(new ShippedDomainEvent(1), Token);
            await publisher.Publish([], Token);
            await publisher.Publish(new List<object>(), Token);

            Assert.Empty(provider.GetRequiredService<EventLog>().Entries);
        }

        [Fact]
        public async Task Struct_event_is_published_to_its_handlers()
        {
            using ServiceProvider provider = BuildProvider(services => services
                .AddTransient<IDomainEventHandler<StockCountedDomainEvent>, StockCountedDomainEventHandler>());
            using IServiceScope scope = provider.CreateScope();
            IDomainEventPublisher publisher = scope.ServiceProvider.GetRequiredService<IDomainEventPublisher>();

            await publisher.Publish(new StockCountedDomainEvent(3), Token);
            await publisher.Publish([new StockCountedDomainEvent(4)], Token);

            Assert.Equal(["stock 3", "stock 4"], provider.GetRequiredService<EventLog>().Entries);
        }

        [Fact]
        public async Task Cancellation_token_reaches_the_handler()
        {
            using ServiceProvider provider = BuildProvider(services => services
                .AddTransient<IDomainEventHandler<DeliveredDomainEvent>, DeliveredDomainEventHandler>());
            using IServiceScope scope = provider.CreateScope();
            using var cancellation = new CancellationTokenSource();

            await scope.ServiceProvider.GetRequiredService<IDomainEventPublisher>().Publish(new DeliveredDomainEvent(1), cancellation.Token);

            Assert.Equal([cancellation.Token], provider.GetRequiredService<EventLog>().Tokens);
        }

        [Fact]
        public async Task Handlers_are_resolved_from_the_callers_scope()
        {
            using ServiceProvider provider = BuildProvider(services => services
                .AddScoped<ScopedCounter>()
                .AddTransient<IDomainEventHandler<ShippedDomainEvent>, CountingShippedDomainEventHandler>());
            using IServiceScope scope = provider.CreateScope();

            await scope.ServiceProvider.GetRequiredService<IDomainEventPublisher>().Publish([new ShippedDomainEvent(1), new ShippedDomainEvent(2)], Token);

            Assert.Equal(2, scope.ServiceProvider.GetRequiredService<ScopedCounter>().Count);
        }

        [Fact]
        public async Task Handler_class_registered_twice_handles_the_event_once()
        {
            using ServiceProvider provider = BuildProvider(services => services
                .AddTransient<IDomainEventHandler<ShippedDomainEvent>, FirstShippedDomainEventHandler>()
                .AddTransient<IDomainEventHandler<ShippedDomainEvent>, FirstShippedDomainEventHandler>());
            using IServiceScope scope = provider.CreateScope();

            await scope.ServiceProvider.GetRequiredService<IDomainEventPublisher>().Publish(new ShippedDomainEvent(1), Token);

            Assert.Equal(["first 1"], provider.GetRequiredService<EventLog>().Entries);
        }

        [Fact]
        public async Task Open_generic_handler_registered_by_hand_handles_every_event_its_constraint_accepts()
        {
            // The container closes the handler for an event that satisfies its constraint and skips it for the others;
            // it runs in registration order among the closed handlers, and once although registered twice.
            using ServiceProvider provider = BuildProvider(services => services
                .AddTransient<IDomainEventHandler<ShippedDomainEvent>, FirstShippedDomainEventHandler>()
                .AddTransient(typeof(IDomainEventHandler<>), typeof(AuditDomainEventHandler<>))
                .AddTransient<IDomainEventHandler<ShippedDomainEvent>, SecondShippedDomainEventHandler>()
                .AddTransient(typeof(IDomainEventHandler<>), typeof(AuditDomainEventHandler<>))
                .AddTransient<IDomainEventHandler<DeliveredDomainEvent>, DeliveredDomainEventHandler>());
            provider.GetRequiredService<IStartupValidator>().Validate();
            using IServiceScope scope = provider.CreateScope();

            await scope.ServiceProvider.GetRequiredService<IDomainEventPublisher>()
                .Publish([new ShippedDomainEvent(1), new DeliveredDomainEvent(1), new ReturnedDomainEvent(1)], Token);

            Assert.Equal(
                ["first 1", "audit ShippedDomainEvent 1", "second 1", "delivered 1", "audit ReturnedDomainEvent 1"],
                provider.GetRequiredService<EventLog>().Entries);
        }

        [Fact]
        public async Task Handler_may_add_events_to_the_published_list()
        {
            var events = new List<object> { new ShippedDomainEvent(1) };
            using ServiceProvider provider = BuildProvider(services => services
                .AddSingleton(events)
                .AddTransient<IDomainEventHandler<ShippedDomainEvent>, DeliveringShippedDomainEventHandler>()
                .AddTransient<IDomainEventHandler<DeliveredDomainEvent>, DeliveredDomainEventHandler>());
            using IServiceScope scope = provider.CreateScope();

            await scope.ServiceProvider.GetRequiredService<IDomainEventPublisher>().Publish(events, Token);

            Assert.Equal([new ShippedDomainEvent(1), new DeliveredDomainEvent(1)], events);
            Assert.Empty(provider.GetRequiredService<EventLog>().Entries);
        }

        [Fact]
        public void Publisher_is_scoped_so_a_singleton_depending_on_it_fails_validation_on_build()
        {
            IServiceCollection services = new ServiceCollection()
                .AddSingleton<PublisherConsumer>()
                .AddPheme(_ => { });

            AggregateException exception = Assert.Throws<AggregateException>(
                () => services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }));

            Assert.Equal(
                ServiceLifetime.Scoped, Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IDomainEventPublisher)).Lifetime);
            Assert.Contains("Cannot consume scoped service 'Pheme.IDomainEventPublisher'", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void Invalid_arguments_throw_synchronously_before_any_event_is_published()
        {
            using ServiceProvider provider = BuildProvider(services => services
                .AddTransient<IDomainEventHandler<ShippedDomainEvent>, FirstShippedDomainEventHandler>());
            using IServiceScope scope = provider.CreateScope();
            IDomainEventPublisher publisher = scope.ServiceProvider.GetRequiredService<IDomainEventPublisher>();
            List<StockCountedDomainEvent> structEvents = [new StockCountedDomainEvent(1)];

            Assert.Throws<ArgumentNullException>("domainEvent", () => { _ = publisher.Publish((object)null!, Token); });
            Assert.Throws<ArgumentNullException>("domainEvents", () => { _ = publisher.Publish((IEnumerable<object>)null!, Token); });
            Assert.Throws<ArgumentException>("domainEvents", () => { _ = publisher.Publish([new ShippedDomainEvent(1), null!], Token); });
            Assert.Throws<ArgumentException>("domainEvents", () => { _ = publisher.Publish([new ShippedDomainEvent(1), new[] { new ShippedDomainEvent(2) }], Token); });
            ArgumentException collection = Assert.Throws<ArgumentException>("domainEvent", () => { _ = publisher.Publish(structEvents, Token); });

            Assert.Contains("'System.Collections.Generic.List<Pheme.UnitTests.StockCountedDomainEvent>' is a collection", collection.Message, StringComparison.Ordinal);
            Assert.Empty(provider.GetRequiredService<EventLog>().Entries);
        }

        private static ServiceProvider BuildFixturesProvider()
        {
            return new ServiceCollection()
                .AddSingleton<Journal>()
                .AddPheme(options => options.AddHandlersFromAssemblyContaining<EntryAddedDomainEvent>())
                .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        }

        private static ServiceProvider BuildProvider(Action<IServiceCollection> configure)
        {
            IServiceCollection services = new ServiceCollection()
                .AddSingleton<EventLog>()
                .AddPheme(_ => { });
            configure(services);
            return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        }
    }

    internal sealed class EventLog
    {
        public List<string> Entries { get; } = [];

        public List<CancellationToken> Tokens { get; } = [];
    }

    internal interface IAuditedDomainEvent
    {
        int OrderId { get; }
    }

    internal sealed record ShippedDomainEvent(int OrderId) : IAuditedDomainEvent;

    internal sealed record ReturnedDomainEvent(int OrderId) : IAuditedDomainEvent;

    internal sealed record DeliveredDomainEvent(int OrderId);

    internal readonly record struct StockCountedDomainEvent(int Quantity);

    internal sealed class FirstShippedDomainEventHandler(EventLog log) : IDomainEventHandler<ShippedDomainEvent>
    {
        public Task Handle(ShippedDomainEvent domainEvent, CancellationToken cancellationToken)
        {
            log.Entries.Add($"first {domainEvent.OrderId}");
            return Task.CompletedTask;
        }
    }

    internal sealed class SecondShippedDomainEventHandler(EventLog log) : IDomainEventHandler<ShippedDomainEvent>
    {
        public Task Handle(ShippedDomainEvent domainEvent, CancellationToken cancellationToken)
        {
            log.Entries.Add($"second {domainEvent.OrderId}");
            return Task.CompletedTask;
        }
    }

    internal sealed class SlowShippedDomainEventHandler(EventLog log) : IDomainEventHandler<ShippedDomainEvent>
    {
        public async Task Handle(ShippedDomainEvent domainEvent, CancellationToken cancellationToken)
        {
            log.Entries.Add($"slow started {domainEvent.OrderId}");
            await Task.Delay(20, cancellationToken);
            log.Entries.Add($"slow finished {domainEvent.OrderId}");
        }
    }

    internal sealed record ShippingFailure(InvalidOperationException Exception, bool ThrowAfterAwait);

    internal sealed class FailingShippedDomainEventHandler(ShippingFailure failure) : IDomainEventHandler<ShippedDomainEvent>
    {
        public Task Handle(ShippedDomainEvent domainEvent, CancellationToken cancellationToken)
        {
            return failure.ThrowAfterAwait ? ThrowAfterAwait() : throw failure.Exception;
        }

        private async Task ThrowAfterAwait()
        {
            await Task.Yield();
            throw failure.Exception;
        }
    }

    internal sealed class DeliveredDomainEventHandler(EventLog log) : IDomainEventHandler<DeliveredDomainEvent>
    {
        public Task Handle(DeliveredDomainEvent domainEvent, CancellationToken cancellationToken)
        {
            log.Entries.Add($"delivered {domainEvent.OrderId}");
            log.Tokens.Add(cancellationToken);
            return Task.CompletedTask;
        }
    }

    internal sealed class StockCountedDomainEventHandler(EventLog log) : IDomainEventHandler<StockCountedDomainEvent>
    {
        public Task Handle(StockCountedDomainEvent domainEvent, CancellationToken cancellationToken)
        {
            log.Entries.Add($"stock {domainEvent.Quantity}");
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Handles every <see cref="IAuditedDomainEvent"/>: registered by hand as an open generic, as the scan skips open
    /// generics.
    /// </summary>
    internal sealed class AuditDomainEventHandler<TEvent>(EventLog log) : IDomainEventHandler<TEvent>
        where TEvent : IAuditedDomainEvent
    {
        public Task Handle(TEvent domainEvent, CancellationToken cancellationToken)
        {
            log.Entries.Add($"audit {typeof(TEvent).Name} {domainEvent.OrderId}");
            return Task.CompletedTask;
        }
    }

    internal sealed class ScopedCounter
    {
        public int Count { get; set; }
    }

    internal sealed class CountingShippedDomainEventHandler(ScopedCounter counter) : IDomainEventHandler<ShippedDomainEvent>
    {
        public Task Handle(ShippedDomainEvent domainEvent, CancellationToken cancellationToken)
        {
            counter.Count++;
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Raises a follow-up event the way an entity would: by adding it to the list being published.
    /// </summary>
    internal sealed class DeliveringShippedDomainEventHandler(List<object> events) : IDomainEventHandler<ShippedDomainEvent>
    {
        public Task Handle(ShippedDomainEvent domainEvent, CancellationToken cancellationToken)
        {
            events.Add(new DeliveredDomainEvent(domainEvent.OrderId));
            return Task.CompletedTask;
        }
    }

    internal sealed class PublisherConsumer(IDomainEventPublisher publisher)
    {
        public IDomainEventPublisher Publisher { get; } = publisher;
    }
}
