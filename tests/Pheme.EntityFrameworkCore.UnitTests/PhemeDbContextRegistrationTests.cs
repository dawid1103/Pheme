using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Pheme.EntityFrameworkCore.UnitTests
{
    public class PhemeDbContextRegistrationTests
    {
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task Several_registrations_for_one_context_add_up()
        {
            using TestShop shop = TestShop.CreateWith(services => services
                .AddPhemeDbContext<ShopDbContext>(TestShop.CollectFromInterface)
                .AddPhemeDbContext<ShopDbContext>(TestShop.CollectFromBaseClass));
            using IServiceScope scope = shop.CreateScope();
            ShopDbContext db = TestShop.Db(scope);
            var order = new Order("order");
            order.Raise(new OrderPlacedDomainEvent("order"));
            db.Orders.Add(order);
            var shipment = new Shipment("parcel");
            shipment.Raise(new ShipmentDispatchedDomainEvent("parcel"));
            db.Shipments.Add(shipment);

            await db.SaveChangesAsync(Token);

            Assert.Equal(["placed: order (0 orders saved)", "dispatched: parcel"], shop.Probe.Log);
        }

        [Fact]
        public void Several_registrations_for_one_context_add_the_interceptor_once()
        {
            using TestShop shop = TestShop.CreateWith(services => services
                .AddPhemeDbContext<ShopDbContext>(TestShop.CollectFromInterface)
                .AddPhemeDbContext<ShopDbContext>(TestShop.CollectFromBaseClass));
            using IServiceScope scope = shop.CreateScope();

            DbContextOptions<ShopDbContext> options = scope.ServiceProvider.GetRequiredService<DbContextOptions<ShopDbContext>>();

            IInterceptor interceptor = Assert.Single(options.FindExtension<CoreOptionsExtension>()?.Interceptors ?? []);
            // The interceptor is internal; its name is enough to tell it from another one.
            Assert.Equal("DomainEventDispatchInterceptor`1", interceptor.GetType().Name);
        }

        [Fact]
        public async Task A_context_that_collects_no_domain_events_gets_no_interceptor_and_needs_no_AddPheme()
        {
            using var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();
            var services = new ServiceCollection();
            services.AddDbContext<ShopDbContext>(options => options.UseSqlite(connection));
            services.AddPhemeDbContext<ShopDbContext>(_ => { });
            using ServiceProvider provider = services.BuildServiceProvider();
            using IServiceScope scope = provider.CreateScope();
            ShopDbContext db = TestShop.Db(scope);
            await db.Database.EnsureCreatedAsync(Token);
            var order = new Order("order");
            order.Raise(new OrderPlacedDomainEvent("order"));
            db.Orders.Add(order);

            await db.SaveChangesAsync(Token);

            DbContextOptions<ShopDbContext> options = scope.ServiceProvider.GetRequiredService<DbContextOptions<ShopDbContext>>();
            Assert.Empty(options.FindExtension<CoreOptionsExtension>()?.Interceptors ?? []);
            Assert.Single(order.DomainEvents);
        }

        [Fact]
        public async Task Collecting_in_a_later_call_adds_the_interceptor()
        {
            using TestShop shop = TestShop.CreateWith(services => services
                .AddPhemeDbContext<ShopDbContext>(_ => { })
                .AddPhemeDbContext<ShopDbContext>(TestShop.CollectFromInterface));
            using IServiceScope scope = shop.CreateScope();
            ShopDbContext db = TestShop.Db(scope);
            var order = new Order("order");
            order.Raise(new OrderPlacedDomainEvent("order"));
            db.Orders.Add(order);

            await db.SaveChangesAsync(Token);

            Assert.Equal(["placed: order (0 orders saved)"], shop.Probe.Log);
        }

        [Fact]
        public void The_configuration_is_applied_by_the_call()
        {
            bool configured = false;

            new ServiceCollection().AddPhemeDbContext<ShopDbContext>(_ => configured = true);

            Assert.True(configured);
        }

        [Fact]
        public async Task AddPhemeDbContext_after_AddDbContext_works_as_well()
        {
            using TestShop shop = TestShop.CreateWith(
                services => services.AddPhemeDbContext<ShopDbContext>(TestShop.CollectFromInterface),
                addAfterDbContext: true);
            using IServiceScope scope = shop.CreateScope();
            ShopDbContext db = TestShop.Db(scope);
            var order = new Order("order");
            order.Raise(new OrderPlacedDomainEvent("order"));
            db.Orders.Add(order);

            await db.SaveChangesAsync(Token);

            Assert.Equal(["placed: order (0 orders saved)"], shop.Probe.Log);
        }

        [Fact]
        public async Task A_registration_for_another_context_does_not_make_this_one_publish()
        {
            using TestShop shop = TestShop.CreateWith(services => services
                .AddPhemeDbContext<OtherDbContext>(TestShop.CollectFromInterface));
            using IServiceScope scope = shop.CreateScope();
            ShopDbContext db = TestShop.Db(scope);
            var order = new Order("order");
            order.Raise(new OrderPlacedDomainEvent("order"));
            db.Orders.Add(order);

            await db.SaveChangesAsync(Token);

            Assert.Empty(shop.Probe.Log);
            Assert.Single(order.DomainEvents);
        }

        [Fact]
        public async Task Each_context_collects_from_its_own_entity_types()
        {
            using TestShop shop = TestShop.CreateWith(services => services
                .AddPhemeDbContext<ShopDbContext>(TestShop.CollectFromInterface)
                .AddPhemeDbContext<OtherDbContext>(TestShop.CollectFromBaseClass));
            using IServiceScope scope = shop.CreateScope();
            ShopDbContext db = TestShop.Db(scope);
            var order = new Order("order");
            order.Raise(new OrderPlacedDomainEvent("order"));
            db.Orders.Add(order);
            var shipment = new Shipment("parcel");
            shipment.Raise(new ShipmentDispatchedDomainEvent("parcel"));
            db.Shipments.Add(shipment);

            await db.SaveChangesAsync(Token);

            Assert.Equal(["placed: order (0 orders saved)"], shop.Probe.Log);
            Assert.Single(shipment.Events);
        }

        [Fact]
        public async Task Registering_an_entity_type_again_replaces_its_delegates()
        {
            using TestShop shop = TestShop.CreateWith(services => services
                .AddPhemeDbContext<ShopDbContext>(options => options.CollectDomainEventsFrom<IHasDomainEvents>(_ => [], _ => { }))
                .AddPhemeDbContext<ShopDbContext>(TestShop.CollectFromInterface)
                .AddPhemeDbContext<ShopDbContext>(TestShop.CollectFromInterface));
            using IServiceScope scope = shop.CreateScope();
            ShopDbContext db = TestShop.Db(scope);
            var order = new Order("order");
            order.Raise(new OrderPlacedDomainEvent("order"));
            db.Orders.Add(order);

            await db.SaveChangesAsync(Token);

            Assert.Equal(["placed: order (0 orders saved)"], shop.Probe.Log);
        }

        [Fact]
        public void Creating_the_context_without_AddPheme_names_the_missing_call()
        {
            using var connection = new SqliteConnection("DataSource=:memory:");
            var services = new ServiceCollection();
            services.AddDbContext<ShopDbContext>(options => options.UseSqlite(connection));
            services.AddPhemeDbContext<ShopDbContext>(TestShop.CollectFromInterface);
            using ServiceProvider provider = services.BuildServiceProvider();
            using IServiceScope scope = provider.CreateScope();

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => TestShop.Db(scope));

            Assert.Contains("AddPheme", exception.Message, StringComparison.Ordinal);
            Assert.Contains(nameof(ShopDbContext), exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void MaxDomainEventRounds_defaults_to_10()
        {
            Assert.Equal(10, new PhemeDbContextOptions().MaxDomainEventRounds);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void MaxDomainEventRounds_below_1_is_rejected(int value)
        {
            var options = new PhemeDbContextOptions();

            Assert.Throws<ArgumentOutOfRangeException>(() => options.MaxDomainEventRounds = value);
            Assert.Equal(10, options.MaxDomainEventRounds);
        }

        [Fact]
        public void Null_arguments_are_rejected()
        {
            var options = new PhemeDbContextOptions();

            Assert.Throws<ArgumentNullException>("services", () => ((IServiceCollection)null!).AddPhemeDbContext<ShopDbContext>(_ => { }));
            Assert.Throws<ArgumentNullException>("configure", () => new ServiceCollection().AddPhemeDbContext<ShopDbContext>(null!));
            Assert.Throws<ArgumentNullException>("getDomainEvents", () => options.CollectDomainEventsFrom<Order>(null!, _ => { }));
            Assert.Throws<ArgumentNullException>("clearDomainEvents", () => options.CollectDomainEventsFrom<Order>(_ => [], null!));
        }

        private sealed class OtherDbContext(DbContextOptions<OtherDbContext> options) : DbContext(options);
    }
}
