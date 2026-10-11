using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Pheme.EntityFrameworkCore.IntegrationTests
{
    /// <summary>
    /// The outbox store on a supported database, run by a derived class per database.
    /// </summary>
    public abstract class OutboxStoreTests(ITestDatabase database)
    {
        private const int MaxAttempts = 2;

        private static readonly DateTimeOffset OccurredOn = new(2026, 10, 10, 14, 30, 0, TimeSpan.Zero);

        private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 10, 11, 8, 0, 0, TimeSpan.Zero));

        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task Pending_messages_are_the_oldest_unprocessed_ones_up_to_the_batch_size()
        {
            await using OutboxApp app = await Ordering();
            OrderSubmittedIntegrationEvent processed = Submitted(OccurredOn.AddMinutes(-1));
            OrderSubmittedIntegrationEvent first = Submitted(OccurredOn);
            OrderSubmittedIntegrationEvent second = Submitted(OccurredOn.AddMinutes(1));
            await Write<OrderingDbContext, OrderingModule>(app, Submitted(OccurredOn.AddMinutes(2)), second, processed, first);
            await OnStore(app, store => store.MarkAsProcessed(processed.EventId, Token));

            IReadOnlyList<OutboxMessage> pending = await OnStore(app, store => store.GetPending(2, MaxAttempts, time.GetUtcNow(), Token));

            Assert.Equal([first.EventId, second.EventId], pending.Select(message => message.Id));
        }

        [Fact]
        public async Task Pending_messages_of_the_same_time_are_in_the_order_of_their_identifiers()
        {
            await using OutboxApp app = await Ordering();

            // Saved one by one against the order of their identifiers (a save inserts in the order of the keys), so the
            // order of the rows does not pass for it.
            OrderSubmittedIntegrationEvent[] submitted = [.. Enumerable.Range(0, 4).Select(_ => Submitted(OccurredOn)).OrderByDescending(e => e.EventId)];
            foreach (OrderSubmittedIntegrationEvent e in submitted)
            {
                await Write<OrderingDbContext, OrderingModule>(app, e);
            }

            Guid[] ordered = [.. submitted.Select(e => e.EventId).Order()];

            Assert.Equal(ordered, (await OnStore(app, store => store.GetPending(10, MaxAttempts, time.GetUtcNow(), Token))).Select(message => message.Id));
            Assert.Equal(ordered, (await OnStore(app, store => store.GetByType(typeof(OrderSubmittedIntegrationEvent).FullName!, Token))).Select(message => message.Id));
            foreach (OrderSubmittedIntegrationEvent e in submitted)
            {
                await OnStore(app, store => store.MarkAsFailed(e.EventId, "first", null, null, Token));
                await OnStore(app, store => store.MarkAsFailed(e.EventId, "second", null, null, Token));
            }

            Assert.Equal(ordered, (await OnStore(app, store => store.GetParked(10, MaxAttempts, Token))).Select(message => message.Id));
        }

        [Fact]
        public async Task A_failed_message_is_due_again_at_its_next_attempt_until_requeued()
        {
            await using OutboxApp app = await Ordering();
            OrderSubmittedIntegrationEvent failed = Submitted(OccurredOn);
            OrderSubmittedIntegrationEvent next = Submitted(OccurredOn.AddMinutes(1));
            await Write<OrderingDbContext, OrderingModule>(app, failed, next);

            // The same instant with an offset, which Npgsql, for one, would not write to timestamptz.
            DateTimeOffset nextAttempt = time.GetUtcNow().AddSeconds(10).ToOffset(TimeSpan.FromHours(2));
            await OnStore(app, store => store.MarkAsFailed(failed.EventId, "first", "A", nextAttempt, Token));

            Assert.Equal(nextAttempt, (await app.SavedMessages<OrderingDbContext>())[0].NextAttemptOnUtc);
            Assert.Equal([next.EventId], await PendingAt(time.GetUtcNow().AddSeconds(10).AddTicks(-10)));
            Assert.Equal([failed.EventId, next.EventId], await PendingAt(time.GetUtcNow().AddSeconds(10)));
            OutboxStats stats = await OnStore(app, store => store.GetStats(MaxAttempts, Token));
            Assert.Equal((2, 0, OccurredOn), (stats.Pending, stats.Parked, stats.OldestPendingOccurredOnUtc));

            Assert.True(await OnStore(app, store => store.Requeue(failed.EventId, Token)));

            OutboxMessage requeued = (await app.SavedMessages<OrderingDbContext>())[0];
            Assert.Equal((null, 0, "A"), (requeued.NextAttemptOnUtc, requeued.AttemptCount, requeued.CompletedHandlers));
            Assert.Equal([failed.EventId, next.EventId], await PendingAt(time.GetUtcNow()));

            async Task<Guid[]> PendingAt(DateTimeOffset now)
            {
                return [.. (await OnStore(app, store => store.GetPending(10, MaxAttempts, now, Token))).Select(message => message.Id)];
            }
        }

        [Fact]
        public async Task MarkAsProcessed_records_the_time_and_clears_the_error()
        {
            await using OutboxApp app = await Ordering();
            OrderSubmittedIntegrationEvent submitted = Submitted(OccurredOn);
            await Write<OrderingDbContext, OrderingModule>(app, submitted);

            Assert.Equal(1, await OnStore(app, store => store.MarkAsFailed(submitted.EventId, "boom", null, null, Token)));
            await OnStore(app, store => store.MarkAsProcessed(submitted.EventId, Token));

            OutboxMessage message = Assert.Single(await app.SavedMessages<OrderingDbContext>());
            Assert.Equal(time.GetUtcNow(), message.ProcessedOnUtc);
            Assert.Null(message.Error);
            Assert.Equal(1, message.AttemptCount);
            Assert.Empty(await OnStore(app, store => store.GetPending(10, MaxAttempts, time.GetUtcNow(), Token)));
        }

        [Fact]
        public async Task Failed_attempts_park_a_message_until_it_is_requeued()
        {
            await using OutboxApp app = await Ordering();
            OrderSubmittedIntegrationEvent parked = Submitted(OccurredOn);
            OrderSubmittedIntegrationEvent pending = Submitted(OccurredOn.AddMinutes(1));
            await Write<OrderingDbContext, OrderingModule>(app, parked, pending);

            Assert.Equal(1, await OnStore(app, store => store.MarkAsFailed(parked.EventId, "first", "A", null, Token)));
            Assert.Equal(2, await OnStore(app, store => store.MarkAsFailed(parked.EventId, "second", "A\nB", null, Token)));

            Assert.Equal([pending.EventId], (await OnStore(app, store => store.GetPending(10, MaxAttempts, time.GetUtcNow(), Token))).Select(message => message.Id));
            OutboxMessage message = Assert.Single(await OnStore(app, store => store.GetParked(10, MaxAttempts, Token)));
            Assert.Equal((parked.EventId, "second", "A\nB", 2), (message.Id, message.Error, message.CompletedHandlers, message.AttemptCount));

            Assert.True(await OnStore(app, store => store.Requeue(parked.EventId, Token)));

            Assert.Empty(await OnStore(app, store => store.GetParked(10, MaxAttempts, Token)));
            message = (await OnStore(app, store => store.GetPending(10, MaxAttempts, time.GetUtcNow(), Token)))[0];
            Assert.Equal((parked.EventId, null, "A\nB", 0), (message.Id, message.Error, message.CompletedHandlers, message.AttemptCount));
        }

        [Fact]
        public async Task Requeue_finds_only_a_message_that_is_not_processed()
        {
            await using OutboxApp app = await Ordering();
            OrderSubmittedIntegrationEvent submitted = Submitted(OccurredOn);
            await Write<OrderingDbContext, OrderingModule>(app, submitted);
            await OnStore(app, store => store.MarkAsProcessed(submitted.EventId, Token));

            Assert.False(await OnStore(app, store => store.Requeue(submitted.EventId, Token)));
            Assert.False(await OnStore(app, store => store.Requeue(Guid.NewGuid(), Token)));
        }

        [Fact]
        public async Task Only_processed_messages_before_the_time_are_deleted()
        {
            await using OutboxApp app = await Ordering();
            OrderSubmittedIntegrationEvent old = Submitted(OccurredOn);
            OrderSubmittedIntegrationEvent recent = Submitted(OccurredOn.AddMinutes(1));
            OrderSubmittedIntegrationEvent parked = Submitted(OccurredOn.AddMinutes(2));
            OrderSubmittedIntegrationEvent pending = Submitted(OccurredOn.AddMinutes(3));
            await Write<OrderingDbContext, OrderingModule>(app, old, recent, parked, pending);
            await OnStore(app, store => store.MarkAsProcessed(old.EventId, Token));
            time.Advance(TimeSpan.FromDays(1));
            await OnStore(app, store => store.MarkAsProcessed(recent.EventId, Token));
            await OnStore(app, store => store.MarkAsFailed(parked.EventId, "first", null, null, Token));
            await OnStore(app, store => store.MarkAsFailed(parked.EventId, "second", null, null, Token));

            // The same instant with an offset, which Npgsql, for one, would not write to timestamptz.
            DateTimeOffset now = time.GetUtcNow().ToOffset(TimeSpan.FromHours(2));
            Assert.Equal(1, await OnStore(app, store => store.DeleteProcessedBefore(now, Token)));

            Assert.Equal(
                [recent.EventId, parked.EventId, pending.EventId],
                (await app.SavedMessages<OrderingDbContext>()).Select(message => message.Id));
        }

        [Fact]
        public async Task Stats_count_the_pending_and_parked_messages()
        {
            await using OutboxApp app = await Ordering();

            OutboxStats empty = await OnStore(app, store => store.GetStats(MaxAttempts, Token));
            Assert.Equal((0, 0, null), (empty.Pending, empty.Parked, empty.OldestPendingOccurredOnUtc));

            OrderSubmittedIntegrationEvent processed = Submitted(OccurredOn);
            OrderSubmittedIntegrationEvent parked = Submitted(OccurredOn.AddMinutes(1));
            await Write<OrderingDbContext, OrderingModule>(
                app, processed, parked, Submitted(OccurredOn.AddMinutes(3)), Submitted(OccurredOn.AddMinutes(2)));
            await OnStore(app, store => store.MarkAsProcessed(processed.EventId, Token));
            await OnStore(app, store => store.MarkAsFailed(parked.EventId, "first", null, null, Token));
            await OnStore(app, store => store.MarkAsFailed(parked.EventId, "second", null, null, Token));

            OutboxStats stats = await OnStore(app, store => store.GetStats(MaxAttempts, Token));
            Assert.Equal((2, 1, OccurredOn.AddMinutes(2)), (stats.Pending, stats.Parked, stats.OldestPendingOccurredOnUtc));
        }

        [Fact]
        public async Task Messages_of_a_type_are_read_in_every_state()
        {
            await using OutboxApp app = await Ordering();
            OrderSubmittedIntegrationEvent processed = Submitted(OccurredOn.AddMinutes(1));
            OrderSubmittedIntegrationEvent parked = Submitted(OccurredOn);
            OrderSubmittedIntegrationEvent pending = Submitted(OccurredOn.AddMinutes(2));
            var express = new ExpressOrderSubmittedIntegrationEvent(Guid.NewGuid(), OccurredOn, "order", "courier");
            await Write<OrderingDbContext, OrderingModule>(app, pending, express, processed, parked);
            await OnStore(app, store => store.MarkAsProcessed(processed.EventId, Token));
            await OnStore(app, store => store.MarkAsFailed(parked.EventId, "first", null, null, Token));
            await OnStore(app, store => store.MarkAsFailed(parked.EventId, "second", null, null, Token));

            IReadOnlyList<OutboxMessage> messages = await OnStore(app, store => store.GetByType(typeof(OrderSubmittedIntegrationEvent).FullName!, Token));

            Assert.Equal([parked.EventId, processed.EventId, pending.EventId], messages.Select(message => message.Id));
        }

        [Fact]
        public async Task Redeliver_makes_a_message_pending_as_if_just_written()
        {
            await using OutboxApp app = await Ordering();
            OrderSubmittedIntegrationEvent submitted = Submitted(OccurredOn);
            await Write<OrderingDbContext, OrderingModule>(app, submitted);
            await OnStore(app, store => store.MarkAsFailed(submitted.EventId, "boom", "A", time.GetUtcNow().AddDays(1), Token));
            await OnStore(app, store => store.MarkAsProcessed(submitted.EventId, Token));

            Assert.True(await OnStore(app, store => store.Redeliver(submitted.EventId, Token)));

            OutboxMessage message = Assert.Single(await OnStore(app, store => store.GetPending(10, MaxAttempts, time.GetUtcNow(), Token)));
            Assert.Equal(
                (submitted.EventId, null, null, null, null, 0),
                (message.Id, message.ProcessedOnUtc, message.Error, message.CompletedHandlers, message.NextAttemptOnUtc, message.AttemptCount));
            Assert.False(await OnStore(app, store => store.Redeliver(Guid.NewGuid(), Token)));
        }

        [Fact]
        public async Task A_store_ignores_the_messages_of_other_sources_in_its_table()
        {
            await using OutboxApp app = await Ordering();
            var legacy = new OutboxMessage
            {
                Id = Guid.NewGuid(),
                Type = typeof(OrderSubmittedIntegrationEvent).FullName!,
                Content = "{}",
                Source = "Legacy",
                OccurredOnUtc = OccurredOn,
            };
            using (IServiceScope scope = app.CreateScope())
            {
                OrderingDbContext db = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();
                db.OutboxMessages.Add(legacy);
                await db.SaveChangesAsync(Token);
            }

            Assert.Empty(await OnStore(app, store => store.GetPending(10, MaxAttempts, time.GetUtcNow(), Token)));
            Assert.Equal(0, (await OnStore(app, store => store.GetStats(MaxAttempts, Token))).Pending);
            Assert.False(await OnStore(app, store => store.Requeue(legacy.Id, Token)));
            Assert.Empty(await OnStore(app, store => store.GetByType(legacy.Type, Token)));
            Assert.False(await OnStore(app, store => store.Redeliver(legacy.Id, Token)));
        }

        [Fact]
        public async Task Each_context_has_a_store_of_its_own_table()
        {
            await using OutboxApp app = await OutboxApp.Create(database, services => services
                .AddPhemeDbContext<OrderingDbContext>(pheme => pheme.UseOutbox<OrderingModule>())
                .AddPhemeDbContext<ShippingDbContext>(pheme => pheme.UseOutbox<ShippingModule>()));
            OrderSubmittedIntegrationEvent submitted = Submitted(OccurredOn);
            var sent = new ParcelSentIntegrationEvent(Guid.NewGuid(), OccurredOn);
            await Write<OrderingDbContext, OrderingModule>(app, submitted);
            await Write<ShippingDbContext, ShippingModule>(app, sent);

            using IServiceScope scope = app.CreateScope();
            IOutboxStore[] stores = [.. scope.ServiceProvider.GetServices<IOutboxStore>()];

            Assert.Equal(["Ordering", "Shipping"], stores.Select(store => store.Source));
            Assert.Equal([submitted.EventId], (await stores[0].GetPending(10, MaxAttempts, time.GetUtcNow(), Token)).Select(message => message.Id));
            Assert.Equal([sent.EventId], (await stores[1].GetPending(10, MaxAttempts, time.GetUtcNow(), Token)).Select(message => message.Id));
        }

        [Fact]
        public async Task Modules_sharing_a_context_have_one_store_and_each_message_is_delivered_once()
        {
            var deliveries = new Deliveries();
            await using OutboxApp app = await OutboxApp.Create(database, services => services
                .AddSingleton(deliveries)
                .AddTransient<IIntegrationEventHandler<OrderSubmittedIntegrationEvent>, RecordingIntegrationEventHandler<OrderSubmittedIntegrationEvent>>()
                .AddTransient<IIntegrationEventHandler<ParcelSentIntegrationEvent>, RecordingIntegrationEventHandler<ParcelSentIntegrationEvent>>()
                .AddPhemeDbContext<OrderingDbContext>(pheme => pheme.UseOutbox<OrderingModule>())
                .AddPhemeDbContext<OrderingDbContext>(pheme => pheme.UseOutbox<BillingModule>()));
            OrderSubmittedIntegrationEvent submitted = Submitted(OccurredOn);
            var sent = new ParcelSentIntegrationEvent(Guid.NewGuid(), OccurredOn.AddMinutes(1));
            using (IServiceScope scope = app.CreateScope())
            {
                Assert.Single(scope.ServiceProvider.GetServices<IOutboxStore>());
                scope.ServiceProvider.GetRequiredService<IOutboxWriter<OrderingModule>>().Add(submitted);
                scope.ServiceProvider.GetRequiredService<IOutboxWriter<BillingModule>>().Add(sent);
                await scope.ServiceProvider.GetRequiredService<OrderingDbContext>().SaveChangesAsync(Token);
            }

            Assert.Equal(2, (await RunOnce(app)).Processed);
            Assert.Equal(0, (await RunOnce(app)).Processed);

            Assert.Equal([submitted.EventId, sent.EventId], deliveries.Handled);
        }

        [Fact]
        public async Task Outbox_delivers_from_the_store_and_retries_a_failed_message()
        {
            var deliveries = new Deliveries { FailuresLeft = 1 };
            await using OutboxApp app = await OutboxApp.Create(database, services => services
                .AddSingleton(deliveries)
                .AddSingleton<TimeProvider>(time)
                .AddTransient<IIntegrationEventHandler<OrderSubmittedIntegrationEvent>, RecordingIntegrationEventHandler<OrderSubmittedIntegrationEvent>>()
                .AddTransient<IIntegrationEventHandler<ParcelSentIntegrationEvent>, FlakyIntegrationEventHandler<ParcelSentIntegrationEvent>>()
                .AddPhemeDbContext<OrderingDbContext>(pheme => pheme.UseOutbox<OrderingModule>()));
            var sent = new ParcelSentIntegrationEvent(Guid.NewGuid(), OccurredOn);
            OrderSubmittedIntegrationEvent submitted = Submitted(OccurredOn.AddMinutes(1));
            await Write<OrderingDbContext, OrderingModule>(app, sent, submitted);

            OutboxRunResult first = await RunOnce(app);

            Assert.Equal(1, first.Processed);
            OutboxMessage failed = Assert.Single(first.Failed);
            Assert.Equal((sent.EventId, 1), (failed.Id, failed.AttemptCount));
            OutboxMessage[] saved = await app.SavedMessages<OrderingDbContext>();
            Assert.Equal((1, null), (saved[0].AttemptCount, saved[0].ProcessedOnUtc));
            Assert.Contains("The service is down.", saved[0].Error, StringComparison.Ordinal);
            Assert.Equal(time.GetUtcNow(), saved[1].ProcessedOnUtc);
            Assert.Equal(time.GetUtcNow().AddSeconds(10), saved[0].NextAttemptOnUtc);
            Assert.Equal(0, (await RunOnce(app)).Processed);

            time.Advance(TimeSpan.FromSeconds(10));
            OutboxRunResult second = await RunOnce(app);

            Assert.Equal((1, 0), (second.Processed, second.Failed.Count));
            saved = await app.SavedMessages<OrderingDbContext>();
            Assert.Equal((1, null, time.GetUtcNow()), (saved[0].AttemptCount, saved[0].Error, saved[0].ProcessedOnUtc));
            Assert.Equal([submitted.EventId, sent.EventId], deliveries.Handled);
        }

        [Fact]
        public async Task Each_handler_saves_in_a_scope_of_its_own_so_the_work_of_a_failed_one_is_never_saved()
        {
            await using OutboxApp app = await OutboxApp.Create(database, services => services
                .AddSingleton<TimeProvider>(time)
                .AddTransient<IIntegrationEventHandler<OrderSubmittedIntegrationEvent>, NoteThenFailIntegrationEventHandler<OrderSubmittedIntegrationEvent>>()
                .AddTransient<IIntegrationEventHandler<OrderSubmittedIntegrationEvent>, NoteIntegrationEventHandler<OrderSubmittedIntegrationEvent>>()
                .AddPhemeDbContext<OrderingDbContext>(pheme => pheme.UseOutbox<OrderingModule>()));
            await Write<OrderingDbContext, OrderingModule>(app, Submitted(OccurredOn));

            OutboxMessage failed = Assert.Single((await RunOnce(app)).Failed);

            Assert.EndsWith("\nPheme.EntityFrameworkCore.IntegrationTests.NoteIntegrationEventHandler<Pheme.EntityFrameworkCore.TestFixtures.OrderSubmittedIntegrationEvent>", failed.CompletedHandlers, StringComparison.Ordinal);
            Assert.Equal(["note of the saving handler"], await app.SavedNotes());
        }

        private static OrderSubmittedIntegrationEvent Submitted(DateTimeOffset occurredOn)
        {
            return new OrderSubmittedIntegrationEvent(Guid.NewGuid(), occurredOn, "order");
        }

        private static async Task Write<TContext, TModule>(OutboxApp app, params IIntegrationEvent[] events)
            where TContext : DbContext
        {
            using IServiceScope scope = app.CreateScope();
            IOutboxWriter<TModule> writer = scope.ServiceProvider.GetRequiredService<IOutboxWriter<TModule>>();
            foreach (IIntegrationEvent integrationEvent in events)
            {
                writer.Add(integrationEvent);
            }

            await scope.ServiceProvider.GetRequiredService<TContext>().SaveChangesAsync(Token);
        }

        /// <summary>
        /// Calls the store of the only context in a new scope, as the outbox of Pheme does.
        /// </summary>
        private static async Task<T> OnStore<T>(OutboxApp app, Func<IOutboxStore, Task<T>> call)
        {
            using IServiceScope scope = app.CreateScope();
            return await call(scope.ServiceProvider.GetRequiredService<IOutboxStore>());
        }

        private static async Task OnStore(OutboxApp app, Func<IOutboxStore, Task> call)
        {
            using IServiceScope scope = app.CreateScope();
            await call(scope.ServiceProvider.GetRequiredService<IOutboxStore>());
        }

        private static async Task<OutboxRunResult> RunOnce(OutboxApp app)
        {
            using IServiceScope scope = app.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<IOutboxRunner>().RunOnce(Token);
        }

        private Task<OutboxApp> Ordering()
        {
            return OutboxApp.Create(database, services => services
                .AddSingleton<TimeProvider>(time)
                .AddPhemeDbContext<OrderingDbContext>(pheme => pheme.UseOutbox<OrderingModule>()));
        }
    }
}
