using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Pheme.EntityFrameworkCore.IntegrationTests
{
    /// <summary>
    /// The outbox helpers of Pheme.Testing on a supported database, run by a derived class per database.
    /// </summary>
    public abstract class OutboxTestingTests(ITestDatabase database)
    {
        private static readonly DateTimeOffset OccurredOn = new(2026, 10, 10, 14, 30, 0, TimeSpan.Zero);

        private readonly Deliveries deliveries = new();

        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public void UsePhemeOutboxForTests_turns_the_processor_off_and_the_retry_delay_to_zero_also_before_AddOutbox()
        {
            var services = new ServiceCollection();
            services.UsePhemeOutboxForTests();
            services.AddPheme(options => options.AddOutbox(outbox =>
            {
                outbox.ProcessInBackground = true;
                outbox.RetryDelay = TimeSpan.FromMinutes(1);
            }));
            using ServiceProvider provider = services.BuildServiceProvider();

            OutboxOptions outbox = provider.GetRequiredService<IOptions<OutboxOptions>>().Value;
            Assert.Equal((false, TimeSpan.Zero), (outbox.ProcessInBackground, outbox.RetryDelay));
        }

        [Fact]
        public async Task DrainOutbox_needs_UsePhemeOutboxForTests()
        {
            await using OutboxApp app = await OutboxApp.Create(database, services => services
                .AddPhemeDbContext<OrderingDbContext>(pheme => pheme.UseOutbox<OrderingModule>()));

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => app.Services.DrainOutbox(cancellationToken: Token));

            Assert.Contains("services.UsePhemeOutboxForTests()", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task DrainOutbox_delivers_the_events_handlers_write_until_none_is_left()
        {
            await using OutboxApp app = await App(services => services
                .AddTransient<IIntegrationEventHandler<OrderSubmittedIntegrationEvent>, ReplyingIntegrationEventHandler<OrderSubmittedIntegrationEvent>>()
                .AddTransient<IIntegrationEventHandler<ParcelSentIntegrationEvent>, RecordingIntegrationEventHandler<ParcelSentIntegrationEvent>>());
            OrderSubmittedIntegrationEvent submitted = Submitted("order");
            await Write(app, submitted);

            await app.Services.DrainOutbox(cancellationToken: Token);

            ParcelSentIntegrationEvent sent = Assert.Single(await app.Services.GetOutboxEvents<ParcelSentIntegrationEvent>(cancellationToken: Token));
            Assert.Equal([submitted.EventId, sent.EventId], deliveries.Handled);
            Assert.All(await app.SavedMessages<OrderingDbContext>(), message => Assert.NotNull(message.ProcessedOnUtc));
        }

        [Fact]
        public async Task DrainOutbox_stops_an_endless_chain()
        {
            await using OutboxApp app = await App(services => services
                .AddTransient<IIntegrationEventHandler<ParcelSentIntegrationEvent>, ReplyingIntegrationEventHandler<ParcelSentIntegrationEvent>>());
            await Write(app, new ParcelSentIntegrationEvent(Guid.NewGuid(), OccurredOn));

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => app.Services.DrainOutbox(3, Token));

            Assert.Contains("run 3", exception.Message, StringComparison.Ordinal);
            Assert.Equal(3, deliveries.Handled.Count);
        }

        [Fact]
        public async Task A_failed_message_fails_only_the_first_DrainOutbox_that_sees_it()
        {
            deliveries.FailuresLeft = 2;
            await using OutboxApp app = await App(services => services
                .AddTransient<IIntegrationEventHandler<ParcelSentIntegrationEvent>, FlakyIntegrationEventHandler<ParcelSentIntegrationEvent>>());
            var sent = new ParcelSentIntegrationEvent(Guid.NewGuid(), OccurredOn);
            await Write(app, sent);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => app.Services.DrainOutbox(cancellationToken: Token));

            Assert.Contains($"'shipping.parcel-sent' {sent.EventId} of store 'Ordering', attempt 1", exception.Message, StringComparison.Ordinal);
            Assert.Contains("The service is down.", exception.Message, StringComparison.Ordinal);

            // Fails again, already reported; then succeeds.
            await app.Services.DrainOutbox(cancellationToken: Token);
            Assert.Empty(deliveries.Handled);
            await app.Services.DrainOutbox(cancellationToken: Token);
            Assert.Equal([sent.EventId], deliveries.Handled);
            OutboxMessage message = Assert.Single(await app.SavedMessages<OrderingDbContext>());
            Assert.Equal(2, message.AttemptCount);
            Assert.NotNull(message.ProcessedOnUtc);
        }

        [Fact]
        public async Task GetOutboxEvents_reads_the_events_of_exactly_a_type_in_every_state()
        {
            await using OutboxApp app = await App(services => services
                .AddPhemeDbContext<ShippingDbContext>(pheme => pheme.UseOutbox<ShippingModule>()));
            OrderSubmittedIntegrationEvent processed = Submitted("processed");
            OrderSubmittedIntegrationEvent pending = Submitted("pending") with { OccurredOn = OccurredOn.AddMinutes(1) };
            var sent = new ParcelSentIntegrationEvent(Guid.NewGuid(), OccurredOn);
            await Write(app, pending, processed, new ExpressOrderSubmittedIntegrationEvent(Guid.NewGuid(), OccurredOn, "express", "courier"));
            using (IServiceScope scope = app.CreateScope())
            {
                scope.ServiceProvider.GetRequiredService<IOutboxWriter<ShippingModule>>().Add(sent);
                await scope.ServiceProvider.GetRequiredService<ShippingDbContext>().SaveChangesAsync(Token);
                IOutboxStore ordering = scope.ServiceProvider.GetServices<IOutboxStore>().Single(store => store.Source == "Ordering");
                await ordering.MarkAsProcessed(processed.EventId, Token);
            }

            Assert.Equal([processed, pending], await app.Services.GetOutboxEvents<OrderSubmittedIntegrationEvent>(cancellationToken: Token));
            Assert.Empty(await app.Services.GetOutboxEvents<OrderSubmittedIntegrationEvent>("Shipping", Token));
            Assert.Equal([sent], await app.Services.GetOutboxEvents<ParcelSentIntegrationEvent>("Shipping", Token));
            var exception = await Assert.ThrowsAsync<ArgumentException>(() => app.Services.GetOutboxEvents<ParcelSentIntegrationEvent>("Billing", Token));
            Assert.Contains("'Ordering', 'Shipping'", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Redeliver_delivers_the_matching_processed_events_again()
        {
            await using OutboxApp app = await App(services => services
                .AddTransient<IIntegrationEventHandler<OrderSubmittedIntegrationEvent>, RecordingIntegrationEventHandler<OrderSubmittedIntegrationEvent>>());
            OrderSubmittedIntegrationEvent first = Submitted("first");
            OrderSubmittedIntegrationEvent second = Submitted("second") with { OccurredOn = OccurredOn.AddSeconds(30) };
            await Write(app, first, second);
            await app.Services.DrainOutbox(cancellationToken: Token);
            OrderSubmittedIntegrationEvent pending = Submitted("first") with { OccurredOn = OccurredOn.AddMinutes(1) };
            await Write(app, pending);

            // The pending one matches too, but only processed events are redelivered.
            Assert.Equal(1, await app.Services.Redeliver<OrderSubmittedIntegrationEvent>(submitted => submitted.OrderName == "first", Token));
            await app.Services.DrainOutbox(cancellationToken: Token);

            Assert.Equal([first.EventId, second.EventId, first.EventId, pending.EventId], deliveries.Handled);
        }

        [Fact]
        public async Task A_redelivered_message_that_fails_is_reported_again()
        {
            deliveries.FailuresLeft = 1;
            await using OutboxApp app = await App(services => services
                .AddTransient<IIntegrationEventHandler<ParcelSentIntegrationEvent>, FlakyIntegrationEventHandler<ParcelSentIntegrationEvent>>());
            await Write(app, new ParcelSentIntegrationEvent(Guid.NewGuid(), OccurredOn));
            await Assert.ThrowsAsync<InvalidOperationException>(() => app.Services.DrainOutbox(cancellationToken: Token));
            await app.Services.DrainOutbox(cancellationToken: Token);

            Assert.Equal(1, await app.Services.Redeliver<ParcelSentIntegrationEvent>(_ => true, Token));
            deliveries.FailuresLeft = 1;

            await Assert.ThrowsAsync<InvalidOperationException>(() => app.Services.DrainOutbox(cancellationToken: Token));
        }

        private static OrderSubmittedIntegrationEvent Submitted(string orderName)
        {
            return new OrderSubmittedIntegrationEvent(Guid.NewGuid(), OccurredOn, orderName);
        }

        private static async Task Write(OutboxApp app, params IIntegrationEvent[] events)
        {
            using IServiceScope scope = app.CreateScope();
            IOutboxWriter<OrderingModule> writer = scope.ServiceProvider.GetRequiredService<IOutboxWriter<OrderingModule>>();
            foreach (IIntegrationEvent integrationEvent in events)
            {
                writer.Add(integrationEvent);
            }

            await scope.ServiceProvider.GetRequiredService<OrderingDbContext>().SaveChangesAsync(Token);
        }

        /// <summary>
        /// An application prepared for tests, with the outbox of the ordering context and the given handlers.
        /// </summary>
        private Task<OutboxApp> App(Action<IServiceCollection> addHandlers)
        {
            return OutboxApp.Create(database, services =>
            {
                services
                    .UsePhemeOutboxForTests()
                    .AddSingleton(deliveries)
                    .AddPhemeDbContext<OrderingDbContext>(pheme => pheme.UseOutbox<OrderingModule>());
                addHandlers(services);
            });
        }
    }
}
