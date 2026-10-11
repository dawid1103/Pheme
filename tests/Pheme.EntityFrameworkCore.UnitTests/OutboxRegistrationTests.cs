using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Pheme.EntityFrameworkCore.UnitTests
{
    public class OutboxRegistrationTests
    {
        [Fact]
        public void UseOutbox_without_AddOutbox_fails_on_start()
        {
            using ServiceProvider provider = Services(addOutbox: false)
                .AddPhemeDbContext<OrderingDbContext>(pheme => pheme.UseOutbox<OrderingModule>())
                .BuildServiceProvider();

            OptionsValidationException exception = Assert.Throws<OptionsValidationException>(
                () => provider.GetRequiredService<IStartupValidator>().Validate());

            Assert.Contains("OrderingDbContext writes integration events to its outbox", exception.Message, StringComparison.Ordinal);
            Assert.Contains("options.AddOutbox()", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void UseOutbox_with_AddOutbox_passes_the_start()
        {
            using ServiceProvider provider = Services(addOutbox: true)
                .AddPhemeDbContext<OrderingDbContext>(pheme => pheme.UseOutbox<OrderingModule>())
                .AddPhemeDbContext<ShippingDbContext>(pheme => pheme.UseOutbox<ShippingModule>())
                .BuildServiceProvider();

            provider.GetRequiredService<IStartupValidator>().Validate();
        }

        [Fact]
        public void UseOutbox_without_AddOutbox_fails_when_the_writer_or_the_store_is_resolved_without_a_host()
        {
            using ServiceProvider provider = Services(addOutbox: false)
                .AddPhemeDbContext<OrderingDbContext>(pheme => pheme.UseOutbox<OrderingModule>())
                .BuildServiceProvider();
            using IServiceScope scope = provider.CreateScope();

            OptionsValidationException exception = Assert.Throws<OptionsValidationException>(
                () => scope.ServiceProvider.GetRequiredService<IOutboxWriter<OrderingModule>>());
            OptionsValidationException storeException = Assert.Throws<OptionsValidationException>(
                () => scope.ServiceProvider.GetRequiredService<IOutboxStore>());

            Assert.Contains("OrderingDbContext writes integration events to its outbox", exception.Message, StringComparison.Ordinal);
            Assert.Contains("options.AddOutbox()", exception.Message, StringComparison.Ordinal);
            Assert.Equal(exception.Message, storeException.Message);
        }

        [Fact]
        public void The_same_module_in_two_contexts_fails_on_start()
        {
            using ServiceProvider provider = Services(addOutbox: true)
                .AddPhemeDbContext<OrderingDbContext>(pheme => pheme.UseOutbox<OrderingModule>().UseOutbox<BillingModule>())
                .AddPhemeDbContext<ShippingDbContext>(pheme => pheme.UseOutbox<ShippingModule>().UseOutbox<BillingModule>())
                .BuildServiceProvider();

            OptionsValidationException exception = Assert.Throws<OptionsValidationException>(
                () => provider.GetRequiredService<IStartupValidator>().Validate());

            Assert.Equal(
                "The module Pheme.EntityFrameworkCore.TestFixtures.BillingModule writes to the outboxes of "
                + "Pheme.EntityFrameworkCore.TestFixtures.OrderingDbContext, Pheme.EntityFrameworkCore.TestFixtures.ShippingDbContext "
                + "(UseOutbox<BillingModule>() in their AddPhemeDbContext), but its IOutboxWriter<BillingModule> writes to "
                + "one context only. Keep the call only for the context that saves the module's changes.",
                exception.Message);
        }

        [Fact]
        public void The_same_source_in_two_contexts_fails_on_start()
        {
            using ServiceProvider provider = Services(addOutbox: true)
                .AddPhemeDbContext<OrderingDbContext>(pheme => pheme.UseOutbox<OrderingModule>())
                .AddPhemeDbContext<ShippingDbContext>(pheme => pheme.UseOutbox<ShippingModule>())
                .AddPhemeDbContext<ShippingDbContext>(pheme => pheme.OutboxSource = "Ordering")
                .BuildServiceProvider();

            OptionsValidationException exception = Assert.Throws<OptionsValidationException>(
                () => provider.GetRequiredService<IStartupValidator>().Validate());

            Assert.Equal(
                "The outboxes of Pheme.EntityFrameworkCore.TestFixtures.OrderingDbContext, "
                + "Pheme.EntityFrameworkCore.TestFixtures.ShippingDbContext have the same source 'Ordering', but the source names "
                + "one outbox in its messages, its delivery and its maintenance. Set PhemeDbContextOptions.OutboxSource in "
                + "AddPhemeDbContext of all but one of them.",
                exception.Message);
        }

        [Fact]
        public void The_same_source_in_two_contexts_fails_when_a_store_is_resolved_without_a_host()
        {
            using ServiceProvider provider = Services(addOutbox: true)
                .AddPhemeDbContext<OrderingDbContext>(pheme => pheme.UseOutbox<OrderingModule>())
                .AddPhemeDbContext<ShippingDbContext>(pheme =>
                {
                    pheme.OutboxSource = "Ordering";
                    pheme.UseOutbox<ShippingModule>();
                })
                .BuildServiceProvider();
            using IServiceScope scope = provider.CreateScope();

            OptionsValidationException exception = Assert.Throws<OptionsValidationException>(
                () => scope.ServiceProvider.GetServices<IOutboxStore>().ToList());

            Assert.Contains("have the same source 'Ordering'", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_context_without_the_outbox_table_names_the_missing_call_on_first_use()
        {
            using var connection = new SqliteConnection("DataSource=:memory:");
            using ServiceProvider provider = Services(addOutbox: true)
                .AddDbContext<ShopDbContext>(options => options.UseSqlite(connection))
                .AddPhemeDbContext<ShopDbContext>(pheme => pheme.UseOutbox<OrderingModule>())
                .BuildServiceProvider();
            using IServiceScope scope = provider.CreateScope();
            IOutboxWriter<OrderingModule> writer = scope.ServiceProvider.GetRequiredService<IOutboxWriter<OrderingModule>>();
            IOutboxStore store = scope.ServiceProvider.GetRequiredService<IOutboxStore>();

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => writer.Add(new ParcelSentIntegrationEvent(Guid.NewGuid(), DateTimeOffset.UtcNow)));
            InvalidOperationException storeException = await Assert.ThrowsAsync<InvalidOperationException>(
                () => store.GetPending(10, 5, DateTimeOffset.UtcNow, TestContext.Current.CancellationToken));

            Assert.Contains(nameof(ShopDbContext), exception.Message, StringComparison.Ordinal);
            Assert.Contains("modelBuilder.AddPhemeOutbox()", exception.Message, StringComparison.Ordinal);
            Assert.Equal(exception.Message, storeException.Message);
        }

        [Fact]
        public async Task A_database_the_outbox_does_not_support_fails_on_first_use()
        {
            using var connection = new SqliteConnection("DataSource=:memory:");
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddPheme(options => options.AddOutbox());
            services.AddDbContext<OrderingDbContext>(options => options.UseSqlite(connection));
            services.AddPhemeDbContext<OrderingDbContext>(pheme => pheme.UseOutbox<OrderingModule>());
            using ServiceProvider provider = services.BuildServiceProvider();
            using IServiceScope scope = provider.CreateScope();
            IOutboxWriter<OrderingModule> writer = scope.ServiceProvider.GetRequiredService<IOutboxWriter<OrderingModule>>();
            IOutboxStore store = scope.ServiceProvider.GetRequiredService<IOutboxStore>();

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => writer.Add(new ParcelSentIntegrationEvent(Guid.NewGuid(), DateTimeOffset.UtcNow)));
            InvalidOperationException storeException = await Assert.ThrowsAsync<InvalidOperationException>(
                () => store.GetPending(10, 5, DateTimeOffset.UtcNow, TestContext.Current.CancellationToken));

            Assert.Equal(
                "OrderingDbContext writes integration events to its outbox on the database provider "
                + "Microsoft.EntityFrameworkCore.Sqlite, but the outbox of Pheme supports only PostgreSQL "
                + "(Npgsql.EntityFrameworkCore.PostgreSQL), the databases its tests run on.",
                exception.Message);
            Assert.Equal(exception.Message, storeException.Message);
        }

        [Fact]
        public void The_same_module_again_for_the_same_context_registers_its_writer_once_and_the_context_one_store()
        {
            IServiceCollection services = Services(addOutbox: true)
                .AddPhemeDbContext<OrderingDbContext>(pheme => pheme.UseOutbox<OrderingModule>().UseOutbox<OrderingModule>())
                .AddPhemeDbContext<OrderingDbContext>(pheme => pheme.UseOutbox<BillingModule>().UseOutbox<OrderingModule>());

            Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IOutboxWriter<OrderingModule>));
            ServiceDescriptor billing = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IOutboxWriter<BillingModule>));
            Assert.Equal(ServiceLifetime.Scoped, billing.Lifetime);
            ServiceDescriptor store = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IOutboxStore));
            Assert.Equal(ServiceLifetime.Scoped, store.Lifetime);
        }

        [Fact]
        public void Invalid_arguments_are_rejected()
        {
            var options = new PhemeDbContextOptions();
            using ServiceProvider provider = Services(addOutbox: true)
                .AddPhemeDbContext<OrderingDbContext>(pheme => pheme.UseOutbox<OrderingModule>())
                .BuildServiceProvider();
            using IServiceScope scope = provider.CreateScope();

            Assert.Throws<ArgumentNullException>(
                "integrationEvent", () => scope.ServiceProvider.GetRequiredService<IOutboxWriter<OrderingModule>>().Add(null!));
            Assert.Throws<ArgumentException>("value", () => options.OutboxSource = "");
            Assert.Throws<ArgumentException>("value", () => options.OutboxSource = " ");
            options.OutboxSource = "Orders";
            options.OutboxSource = null;
            Assert.Null(options.OutboxSource);
        }

        private static IServiceCollection Services(bool addOutbox)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddPheme(options =>
            {
                if (addOutbox)
                {
                    options.AddOutbox();
                }
            });

            // The contexts are never connected.
            services.AddDbContext<OrderingDbContext>(options => options.UseNpgsql("Host=localhost"));
            services.AddDbContext<ShippingDbContext>(options => options.UseNpgsql("Host=localhost"));
            return services;
        }
    }
}
