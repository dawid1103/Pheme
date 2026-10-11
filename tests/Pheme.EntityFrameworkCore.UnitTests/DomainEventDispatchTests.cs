using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Pheme.EntityFrameworkCore.UnitTests
{
    public class DomainEventDispatchTests
    {
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task Event_is_published_before_the_save_and_the_changes_of_its_handler_are_saved_with_it()
        {
            using TestShop shop = TestShop.Create();
            using (IServiceScope scope = shop.CreateScope())
            {
                ShopDbContext db = TestShop.Db(scope);
                db.Orders.Add(Placed("first"));

                await db.SaveChangesAsync(Token);
            }

            Assert.Equal(["placed: first (0 orders saved)"], shop.Probe.Log);
            Assert.Equal(["first"], await shop.SavedOrders());
            Assert.Equal(["placed: first"], await shop.SavedNotes());
        }

        [Fact]
        public async Task Handler_gets_the_saving_DbContext()
        {
            using TestShop shop = TestShop.Create();
            using IServiceScope scope = shop.CreateScope();
            ShopDbContext db = TestShop.Db(scope);
            db.Orders.Add(Placed("first"));

            await db.SaveChangesAsync(Token);

            Assert.Same(db, Assert.Single(shop.Probe.Contexts));
        }

        [Fact]
        public async Task Events_are_published_in_tracking_order_and_in_the_order_of_each_entity()
        {
            using TestShop shop = TestShop.Create();
            using IServiceScope scope = shop.CreateScope();
            ShopDbContext db = TestShop.Db(scope);
            Order first = Placed("a1");
            first.Raise(new OrderPlacedDomainEvent("a2"));
            db.Orders.Add(first);
            db.Orders.Add(Placed("b1"));

            await db.SaveChangesAsync(Token);

            Assert.Equal(
                ["placed: a1 (0 orders saved)", "placed: a2 (0 orders saved)", "placed: b1 (0 orders saved)"],
                shop.Probe.Log);
        }

        [Fact]
        public async Task Handler_exception_reaches_the_caller_unchanged_and_nothing_is_saved()
        {
            using TestShop shop = TestShop.Create();
            using (IServiceScope scope = shop.CreateScope())
            {
                ShopDbContext db = TestShop.Db(scope);
                Order order = Placed("first");
                order.Raise(new OrderRejectedDomainEvent("too late"));
                db.Orders.Add(order);

                RejectedException exception = await Assert.ThrowsAsync<RejectedException>(() => db.SaveChangesAsync(Token));

                Assert.Same(shop.Probe.Thrown, exception);
            }

            Assert.Empty(await shop.SavedOrders());
            Assert.Empty(await shop.SavedNotes());
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Handler_saving_the_publishing_context_fails_the_save_and_nothing_is_saved(bool synchronously)
        {
            using TestShop shop = TestShop.Create();
            using (IServiceScope scope = shop.CreateScope())
            {
                ShopDbContext db = TestShop.Db(scope);
                Order order = Placed("first");
                order.Raise(new NoteSavedDomainEvent("saved by the handler", synchronously));
                order.Raise(new OrderRejectedDomainEvent("too late"));
                db.Orders.Add(order);

                InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync(Token));

                Assert.Equal(
                    "A domain event handler saved ShopDbContext while it was publishing domain events. The save that "
                    + "publishes an event also saves the changes of its handlers, in one transaction; change the context in "
                    + "the handler without saving it, also through a command sent from the handler.",
                    exception.Message);
                Assert.Null(shop.Probe.Thrown);
            }

            Assert.Empty(await shop.SavedOrders());
            Assert.Empty(await shop.SavedNotes());
        }

        [Fact]
        public async Task Handler_saving_another_instance_of_the_context_saves_it()
        {
            using TestShop shop = TestShop.Create();
            using (IServiceScope scope = shop.CreateScope())
            {
                ShopDbContext db = TestShop.Db(scope);
                var order = new Order("first");
                order.Raise(new NoteSavedElsewhereDomainEvent("saved elsewhere"));
                db.Orders.Add(order);

                await db.SaveChangesAsync(Token);

                // Published again after the other save: the context is still the one publishing.
                db.Orders.Add(Placed("second"));
                await db.SaveChangesAsync(Token);
            }

            Assert.Equal(["first", "second"], await shop.SavedOrders());
            Assert.Equal(["saved elsewhere", "placed: second"], await shop.SavedNotes());
        }

        [Fact]
        public async Task Context_saved_after_a_failed_publication_is_not_taken_for_a_publishing_one()
        {
            using TestShop shop = TestShop.Create();
            using IServiceScope scope = shop.CreateScope();
            ShopDbContext db = TestShop.Db(scope);
            Order order = Placed("first");
            order.Raise(new OrderRejectedDomainEvent("too late"));
            db.Orders.Add(order);
            await Assert.ThrowsAsync<RejectedException>(() => db.SaveChangesAsync(Token));

            db.Orders.Add(Placed("second"));
            await db.SaveChangesAsync(Token);

            Assert.Equal(["first", "second"], await shop.SavedOrders());
        }

        [Fact]
        public async Task Event_raised_by_a_handler_is_published_in_the_next_round_before_the_same_save()
        {
            using TestShop shop = TestShop.Create();
            using (IServiceScope scope = shop.CreateScope())
            {
                ShopDbContext db = TestShop.Db(scope);
                db.Orders.Add(new Order("first"));
                await db.SaveChangesAsync(Token);
            }

            using (IServiceScope scope = shop.CreateScope())
            {
                ShopDbContext db = TestShop.Db(scope);
                Order order = await db.Orders.SingleAsync(Token);
                order.Raise(new OrderConfirmedDomainEvent(order));

                await db.SaveChangesAsync(Token);
            }

            Assert.Equal(["confirmed: first", "placed: follow-up (1 orders saved)"], shop.Probe.Log);
            Assert.Equal(["first (confirmed)", "follow-up"], await shop.SavedOrders());
            Assert.Equal(["placed: follow-up"], await shop.SavedNotes());
        }

        [Fact]
        public async Task Handlers_still_raising_events_after_MaxDomainEventRounds_fail_the_save()
        {
            using TestShop shop = TestShop.Create();
            using (IServiceScope scope = shop.CreateScope())
            {
                ShopDbContext db = TestShop.Db(scope);
                db.Orders.Add(Pinged("first"));

                InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync(Token));

                Assert.Contains("after 10 rounds", exception.Message, StringComparison.Ordinal);
                Assert.Contains("cycle", exception.Message, StringComparison.Ordinal);
                Assert.Contains("PhemeDbContextOptions.MaxDomainEventRounds", exception.Message, StringComparison.Ordinal);
            }

            Assert.Equal(Enumerable.Repeat("ping", 10), shop.Probe.Log);
            Assert.Empty(await shop.SavedOrders());
        }

        [Fact]
        public async Task MaxDomainEventRounds_limits_the_rounds()
        {
            using TestShop shop = TestShop.Create(options => options.MaxDomainEventRounds = 3);
            using IServiceScope scope = shop.CreateScope();
            ShopDbContext db = TestShop.Db(scope);
            db.Orders.Add(Pinged("first"));

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync(Token));

            Assert.Contains("after 3 rounds", exception.Message, StringComparison.Ordinal);
            Assert.Equal(["ping", "ping", "ping"], shop.Probe.Log);
        }

        [Fact]
        public async Task Save_without_events_publishes_nothing()
        {
            using TestShop shop = TestShop.Create();
            using (IServiceScope scope = shop.CreateScope())
            {
                ShopDbContext db = TestShop.Db(scope);
                db.Orders.Add(new Order("quiet"));
                db.Shipments.Add(new Shipment("quiet"));
                db.Notes.Add(new Note("plain"));

                await db.SaveChangesAsync(Token);
            }

            Assert.Empty(shop.Probe.Log);
            Assert.Equal(["quiet"], await shop.SavedOrders());
            Assert.Equal(["plain"], await shop.SavedNotes());
        }

        [Fact]
        public async Task Only_entities_with_events_are_cleared()
        {
            List<string> cleared = [];
            using TestShop shop = TestShop.CreateWith(services => services.AddPhemeDbContext<ShopDbContext>(options =>
                options.CollectDomainEventsFrom<Order>(order => order.DomainEvents, order =>
                {
                    cleared.Add(order.Name);
                    order.ClearDomainEvents();
                })));
            using IServiceScope scope = shop.CreateScope();
            ShopDbContext db = TestShop.Db(scope);
            db.Orders.Add(new Order("quiet"));
            db.Orders.Add(Placed("loud"));

            await db.SaveChangesAsync(Token);

            Assert.Equal(["loud"], cleared);
        }

        [Fact]
        public async Task Published_events_are_cleared_so_the_next_save_does_not_publish_them_again()
        {
            using TestShop shop = TestShop.Create();
            using IServiceScope scope = shop.CreateScope();
            ShopDbContext db = TestShop.Db(scope);
            Order order = Placed("first");
            db.Orders.Add(order);
            await db.SaveChangesAsync(Token);

            order.Name = "renamed";
            await db.SaveChangesAsync(Token);

            Assert.Empty(order.DomainEvents);
            Assert.Equal(["placed: first (0 orders saved)"], shop.Probe.Log);
            Assert.Equal(["renamed"], await shop.SavedOrders());
        }

        [Fact]
        public async Task Each_registered_entity_type_is_collected_from_its_own_entities()
        {
            using TestShop shop = TestShop.Create();
            using IServiceScope scope = shop.CreateScope();
            ShopDbContext db = TestShop.Db(scope);
            db.Orders.Add(Placed("order"));
            var shipment = new Shipment("parcel");
            shipment.Raise(new ShipmentDispatchedDomainEvent("parcel"));
            db.Shipments.Add(shipment);

            await db.SaveChangesAsync(Token);

            Assert.Equal(["placed: order (0 orders saved)", "dispatched: parcel"], shop.Probe.Log);
            Assert.Empty(shipment.Events);
        }

        [Fact]
        public async Task Entity_matching_two_registered_types_fails_the_save()
        {
            using TestShop shop = TestShop.Create();
            using IServiceScope scope = shop.CreateScope();
            ShopDbContext db = TestShop.Db(scope);
            var hybrid = new Hybrid("both");
            hybrid.Raise(new ShipmentDispatchedDomainEvent("both"));
            db.Hybrids.Add(hybrid);

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync(Token));

            Assert.Contains(typeof(Hybrid).FullName!, exception.Message, StringComparison.Ordinal);
            Assert.Contains(typeof(IHasDomainEvents).FullName!, exception.Message, StringComparison.Ordinal);
            Assert.Contains(typeof(AggregateRoot).FullName!, exception.Message, StringComparison.Ordinal);
            Assert.Contains(nameof(PhemeDbContextOptions.CollectDomainEventsFrom), exception.Message, StringComparison.Ordinal);
            Assert.Empty(shop.Probe.Log);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Collector_matching_no_entity_type_of_the_model_fails_the_save(bool synchronous)
        {
            using TestShop shop = TestShop.Create(options =>
                options.CollectDomainEventsFrom<IHasEventsOfAnotherModule>(entity => entity.Events, _ => { }));
            using IServiceScope scope = shop.CreateScope();
            ShopDbContext db = TestShop.Db(scope);
            db.Orders.Add(Placed("first"));

            InvalidOperationException exception = synchronous
                ? Assert.Throws<InvalidOperationException>(() => db.SaveChanges())
                : await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync(Token));

            Assert.Equal(
                $"ShopDbContext collects domain events from {typeof(IHasEventsOfAnotherModule).FullName} "
                + "(CollectDomainEventsFrom in AddPhemeDbContext), but no entity type of its model is assignable to it, so "
                + "it would collect none; collect from a type its entities implement or derive from, or register the "
                + "collector for the context that maps them.",
                exception.Message);
            Assert.Empty(shop.Probe.Log);
            Assert.Empty(await shop.SavedOrders());
        }

        [Fact]
        public async Task Null_events_of_an_entity_fail_the_save()
        {
            using TestShop shop = TestShop.CreateWith(services => services.AddPhemeDbContext<ShopDbContext>(options =>
                options.CollectDomainEventsFrom<Order>(_ => null!, _ => { })));
            using IServiceScope scope = shop.CreateScope();
            ShopDbContext db = TestShop.Db(scope);
            db.Orders.Add(new Order("first"));

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync(Token));

            Assert.Contains(typeof(Order).FullName!, exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Synchronous_save_with_events_fails_and_saves_nothing()
        {
            using TestShop shop = TestShop.Create();
            using IServiceScope scope = shop.CreateScope();
            ShopDbContext db = TestShop.Db(scope);
            Order order = Placed("first");
            db.Orders.Add(order);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => db.SaveChanges());

            Assert.Contains("SaveChangesAsync", exception.Message, StringComparison.Ordinal);
            Assert.Empty(shop.Probe.Log);
            Assert.Single(order.DomainEvents);
            Assert.Empty(await shop.SavedOrders());
        }

        [Fact]
        public async Task Synchronous_save_without_events_saves()
        {
            using TestShop shop = TestShop.Create();
            using (IServiceScope scope = shop.CreateScope())
            {
                ShopDbContext db = TestShop.Db(scope);
                db.Orders.Add(new Order("quiet"));

                db.SaveChanges();
            }

            Assert.Equal(["quiet"], await shop.SavedOrders());
        }

        private static Order Placed(string name)
        {
            var order = new Order(name);
            order.Raise(new OrderPlacedDomainEvent(name));
            return order;
        }

        private static Order Pinged(string name)
        {
            var order = new Order(name);
            order.Raise(new PingDomainEvent(order));
            return order;
        }

        /// <summary>
        /// The contract of entities of another module, implemented by none of the entity types of <see cref="ShopDbContext"/>.
        /// </summary>
        private interface IHasEventsOfAnotherModule
        {
            IReadOnlyCollection<object> Events { get; }
        }
    }
}
