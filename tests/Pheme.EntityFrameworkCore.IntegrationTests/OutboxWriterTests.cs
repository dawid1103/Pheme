using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Pheme.EntityFrameworkCore.IntegrationTests
{
    /// <summary>
    /// The outbox writer on a supported database, run by a derived class per database.
    /// </summary>
    public abstract class OutboxWriterTests(ITestDatabase database)
    {
        private static readonly DateTimeOffset OccurredOn = new(2026, 10, 10, 14, 30, 0, TimeSpan.FromHours(2));

        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task Event_without_a_handler_is_rejected_before_it_reaches_the_context()
        {
            await using OutboxApp app = await OutboxApp.Create(database, services => services
                .AddPhemeDbContext<OrderingDbContext>(pheme => pheme.UseOutbox<OrderingModule>()));
            using IServiceScope scope = app.CreateScope();
            OrderingDbContext db = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();
            IOutboxWriter<OrderingModule> writer = scope.ServiceProvider.GetRequiredService<IOutboxWriter<OrderingModule>>();

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => writer.Add(new UnhandledIntegrationEvent(Guid.NewGuid(), OccurredOn)));

            Assert.StartsWith(
                "No handler of integration event 'Pheme.EntityFrameworkCore.TestFixtures.UnhandledIntegrationEvent' is registered",
                exception.Message,
                StringComparison.Ordinal);
            Assert.Empty(db.ChangeTracker.Entries<OutboxMessage>());
        }

        [Fact]
        public async Task Event_is_saved_with_the_change_by_the_next_save()
        {
            await using OutboxApp app = await OutboxApp.Create(database, services => services
                .AddPhemeDbContext<OrderingDbContext>(pheme => pheme.UseOutbox<OrderingModule>()));
            var submitted = new OrderSubmittedIntegrationEvent(Guid.NewGuid(), OccurredOn, "first");
            using (IServiceScope scope = app.CreateScope())
            {
                OrderingDbContext db = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();
                db.Notes.Add(new Note("submitted"));
                scope.ServiceProvider.GetRequiredService<IOutboxWriter<OrderingModule>>().Add(submitted);

                Assert.Empty(await app.SavedMessages<OrderingDbContext>());

                await db.SaveChangesAsync(Token);
            }

            OutboxMessage message = Assert.Single(await app.SavedMessages<OrderingDbContext>());
            Assert.Equal(submitted.EventId, message.Id);
            Assert.Equal(typeof(OrderSubmittedIntegrationEvent).FullName, message.Type);
            Assert.Contains("\"orderName\":\"first\"", message.Content, StringComparison.Ordinal);
            Assert.Equal("Ordering", message.Source);
            Assert.Equal(OccurredOn, message.OccurredOnUtc);
            Assert.Equal(TimeSpan.Zero, message.OccurredOnUtc.Offset);
            Assert.Null(message.ProcessedOnUtc);
            Assert.Equal(0, message.AttemptCount);
            Assert.Equal(["submitted"], await app.SavedNotes());
        }

        [Fact]
        public async Task Rolled_back_transaction_leaves_neither_the_change_nor_the_event()
        {
            await using OutboxApp app = await OutboxApp.Create(database, services => services
                .AddPhemeDbContext<OrderingDbContext>(pheme => pheme.UseOutbox<OrderingModule>()));
            using (IServiceScope scope = app.CreateScope())
            {
                OrderingDbContext db = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();
                await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(Token);
                db.Notes.Add(new Note("submitted"));
                scope.ServiceProvider.GetRequiredService<IOutboxWriter<OrderingModule>>()
                    .Add(new OrderSubmittedIntegrationEvent(Guid.NewGuid(), OccurredOn, "first"));
                await db.SaveChangesAsync(Token);

                await transaction.RollbackAsync(Token);
            }

            Assert.Empty(await app.SavedMessages<OrderingDbContext>());
            Assert.Empty(await app.SavedNotes());
        }

        [Fact]
        public async Task Event_is_stored_under_the_name_of_its_runtime_type_with_its_fields()
        {
            await using OutboxApp app = await OutboxApp.Create(database, services => services
                .AddPhemeDbContext<OrderingDbContext>(pheme => pheme.UseOutbox<OrderingModule>()));
            using (IServiceScope scope = app.CreateScope())
            {
                IOutboxWriter<OrderingModule> writer = scope.ServiceProvider.GetRequiredService<IOutboxWriter<OrderingModule>>();
                OrderSubmittedIntegrationEvent express = new ExpressOrderSubmittedIntegrationEvent(Guid.NewGuid(), OccurredOn, "first", "courier");
                writer.Add(express);
                writer.Add(new ParcelSentIntegrationEvent(Guid.NewGuid(), OccurredOn.AddMinutes(1)));

                await scope.ServiceProvider.GetRequiredService<OrderingDbContext>().SaveChangesAsync(Token);
            }

            OutboxMessage[] messages = await app.SavedMessages<OrderingDbContext>();
            Assert.Equal([typeof(ExpressOrderSubmittedIntegrationEvent).FullName, "shipping.parcel-sent"], messages.Select(message => message.Type));
            Assert.Contains("\"courier\":\"courier\"", messages[0].Content, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Each_module_writes_to_the_outbox_of_its_own_context()
        {
            await using OutboxApp app = await OutboxApp.Create(database, services => services
                .AddPhemeDbContext<OrderingDbContext>(pheme => pheme.UseOutbox<OrderingModule>())
                .AddPhemeDbContext<ShippingDbContext>(pheme => pheme.UseOutbox<ShippingModule>()));
            var submitted = new OrderSubmittedIntegrationEvent(Guid.NewGuid(), OccurredOn, "first");
            var sent = new ParcelSentIntegrationEvent(Guid.NewGuid(), OccurredOn);
            using (IServiceScope scope = app.CreateScope())
            {
                scope.ServiceProvider.GetRequiredService<IOutboxWriter<OrderingModule>>().Add(submitted);
                scope.ServiceProvider.GetRequiredService<IOutboxWriter<ShippingModule>>().Add(sent);

                await scope.ServiceProvider.GetRequiredService<OrderingDbContext>().SaveChangesAsync(Token);
                await scope.ServiceProvider.GetRequiredService<ShippingDbContext>().SaveChangesAsync(Token);
            }

            OutboxMessage ordering = Assert.Single(await app.SavedMessages<OrderingDbContext>());
            Assert.Equal((submitted.EventId, "Ordering"), (ordering.Id, ordering.Source));
            OutboxMessage shipping = Assert.Single(await app.SavedMessages<ShippingDbContext>());
            Assert.Equal((sent.EventId, "Shipping"), (shipping.Id, shipping.Source));
        }

        [Fact]
        public async Task Modules_sharing_a_context_write_to_its_one_outbox()
        {
            await using OutboxApp app = await OutboxApp.Create(database, services => services
                .AddPhemeDbContext<OrderingDbContext>(pheme => pheme.UseOutbox<OrderingModule>().UseOutbox<BillingModule>()));
            using (IServiceScope scope = app.CreateScope())
            {
                scope.ServiceProvider.GetRequiredService<IOutboxWriter<OrderingModule>>()
                    .Add(new OrderSubmittedIntegrationEvent(Guid.NewGuid(), OccurredOn, "first"));
                scope.ServiceProvider.GetRequiredService<IOutboxWriter<BillingModule>>()
                    .Add(new ParcelSentIntegrationEvent(Guid.NewGuid(), OccurredOn.AddMinutes(1)));

                await scope.ServiceProvider.GetRequiredService<OrderingDbContext>().SaveChangesAsync(Token);
            }

            OutboxMessage[] messages = await app.SavedMessages<OrderingDbContext>();
            Assert.Equal(["Ordering", "Ordering"], messages.Select(message => message.Source));
        }

        [Fact]
        public async Task OutboxSource_replaces_the_default_source_and_the_last_value_wins()
        {
            await using OutboxApp app = await OutboxApp.Create(database, services => services
                .AddPhemeDbContext<OrderingDbContext>(pheme =>
                {
                    pheme.OutboxSource = "Sales";
                    pheme.UseOutbox<OrderingModule>();
                })
                .AddPhemeDbContext<OrderingDbContext>(pheme => pheme.OutboxSource = "Orders"));
            using (IServiceScope scope = app.CreateScope())
            {
                scope.ServiceProvider.GetRequiredService<IOutboxWriter<OrderingModule>>()
                    .Add(new OrderSubmittedIntegrationEvent(Guid.NewGuid(), OccurredOn, "first"));

                await scope.ServiceProvider.GetRequiredService<OrderingDbContext>().SaveChangesAsync(Token);
            }

            Assert.Equal("Orders", Assert.Single(await app.SavedMessages<OrderingDbContext>()).Source);
        }

        [Fact]
        public async Task The_same_event_cannot_be_stored_twice()
        {
            await using OutboxApp app = await OutboxApp.Create(database, services => services
                .AddPhemeDbContext<OrderingDbContext>(pheme => pheme.UseOutbox<OrderingModule>()));
            var submitted = new OrderSubmittedIntegrationEvent(Guid.NewGuid(), OccurredOn, "first");
            using (IServiceScope scope = app.CreateScope())
            {
                scope.ServiceProvider.GetRequiredService<IOutboxWriter<OrderingModule>>().Add(submitted);
                await scope.ServiceProvider.GetRequiredService<OrderingDbContext>().SaveChangesAsync(Token);
            }

            using (IServiceScope scope = app.CreateScope())
            {
                IOutboxWriter<OrderingModule> writer = scope.ServiceProvider.GetRequiredService<IOutboxWriter<OrderingModule>>();
                writer.Add(submitted);

                Assert.Throws<InvalidOperationException>(() => writer.Add(submitted));
            }

            using (IServiceScope scope = app.CreateScope())
            {
                scope.ServiceProvider.GetRequiredService<IOutboxWriter<OrderingModule>>().Add(submitted);

                await Assert.ThrowsAsync<DbUpdateException>(
                    () => scope.ServiceProvider.GetRequiredService<OrderingDbContext>().SaveChangesAsync(Token));
            }

            Assert.Single(await app.SavedMessages<OrderingDbContext>());
        }
    }
}
