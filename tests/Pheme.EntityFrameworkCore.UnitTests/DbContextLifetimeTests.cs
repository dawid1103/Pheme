using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Pheme.EntityFrameworkCore.UnitTests
{
    public class DbContextLifetimeTests
    {
        [Theory]
        [InlineData("AddDbContextFactory", "DbContextOptions<ShopDbContext> is singleton")]
        [InlineData("AddDbContextPool", "DbContextOptions<ShopDbContext> is singleton")]
        [InlineData("AddPooledDbContextFactory", "DbContextOptions<ShopDbContext> is singleton")]
        [InlineData("AddDbContext with singleton options", "DbContextOptions<ShopDbContext> is singleton")]
        [InlineData("AddDbContext with transient options", "DbContextOptions<ShopDbContext> is transient")]
        [InlineData("AddDbContextFactory, then AddDbContext", "DbContextOptions<ShopDbContext> is singleton")]
        [InlineData("AddDbContext, then AddDbContextFactory", "IDbContextFactory<ShopDbContext> is singleton")]
        public void A_context_publishing_domain_events_without_scoped_options_fails_on_start(string registration, string problem)
        {
            using var connection = new SqliteConnection("DataSource=:memory:");
            using ServiceProvider provider = Services(connection, registration, TestShop.CollectFromInterface);

            OptionsValidationException exception = Assert.Throws<OptionsValidationException>(
                () => provider.GetRequiredService<IStartupValidator>().Validate());

            Assert.Contains($"its {problem}, so", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void The_error_names_the_context_and_the_registration_that_works()
        {
            using var connection = new SqliteConnection("DataSource=:memory:");
            using ServiceProvider provider = Services(connection, "AddDbContextFactory", TestShop.CollectFromInterface);

            OptionsValidationException exception = Assert.Throws<OptionsValidationException>(
                () => provider.GetRequiredService<IStartupValidator>().Validate());

            Assert.Equal(
                "ShopDbContext publishes domain events (CollectDomainEventsFrom in AddPhemeDbContext) through the scope it "
                + "is created in, but its DbContextOptions<ShopDbContext> is singleton, so it would publish them through "
                + "the root service provider. Register the context with AddDbContext and its default scoped options; "
                + "AddDbContextPool and AddPooledDbContextFactory are not supported, and AddDbContextFactory needs "
                + "ServiceLifetime.Scoped.",
                exception.Message);
        }

        [Fact]
        public void The_check_sees_registrations_made_after_AddPhemeDbContext()
        {
            using var connection = new SqliteConnection("DataSource=:memory:");
            var services = new ServiceCollection();
            services.AddPheme(options => options.AddHandlersFromAssemblyContaining<TestShop>());
            services.AddPhemeDbContext<ShopDbContext>(TestShop.CollectFromInterface);
            services.AddDbContextFactory<ShopDbContext>(options => options.UseSqlite(connection));
            using ServiceProvider provider = services.BuildServiceProvider();

            OptionsValidationException exception = Assert.Throws<OptionsValidationException>(
                () => provider.GetRequiredService<IStartupValidator>().Validate());

            Assert.Contains("its DbContextOptions<ShopDbContext> is singleton", exception.Message, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("AddDbContext")]
        [InlineData("AddDbContextFactory with a scoped lifetime")]
        [InlineData("AddDbContext, then AddDbContextFactory with a scoped lifetime")]
        public void A_context_publishing_domain_events_with_scoped_options_passes_the_start(string registration)
        {
            using var connection = new SqliteConnection("DataSource=:memory:");
            using ServiceProvider provider = Services(connection, registration, TestShop.CollectFromInterface);

            provider.GetRequiredService<IStartupValidator>().Validate();
        }

        [Fact]
        public void A_context_that_collects_no_domain_events_may_have_any_options()
        {
            using var connection = new SqliteConnection("DataSource=:memory:");
            using ServiceProvider provider = Services(connection, "AddDbContextPool", _ => { });

            provider.GetRequiredService<IStartupValidator>().Validate();
        }

        [Fact]
        public void Without_a_host_creating_the_context_from_a_singleton_factory_fails_with_the_same_error()
        {
            using var connection = new SqliteConnection("DataSource=:memory:");
            using ServiceProvider provider = Services(connection, "AddDbContextFactory", TestShop.CollectFromInterface);
            string expected = Assert.Throws<OptionsValidationException>(
                () => provider.GetRequiredService<IStartupValidator>().Validate()).Message;

            // Not the container's error about resolving the scoped interceptor from the root provider.
            OptionsValidationException exception = Assert.Throws<OptionsValidationException>(
                () => provider.GetRequiredService<IDbContextFactory<ShopDbContext>>().CreateDbContext());

            Assert.Equal(expected, exception.Message);
        }

        [Fact]
        public void Without_a_host_resolving_a_pooled_context_fails_with_the_same_error()
        {
            using var connection = new SqliteConnection("DataSource=:memory:");
            using ServiceProvider provider = Services(connection, "AddDbContextPool", TestShop.CollectFromInterface);
            using IServiceScope scope = provider.CreateScope();

            OptionsValidationException exception = Assert.Throws<OptionsValidationException>(() => TestShop.Db(scope));

            Assert.Contains("its DbContextOptions<ShopDbContext> is singleton", exception.Message, StringComparison.Ordinal);
        }

        private static ServiceProvider Services(
            SqliteConnection connection,
            string registration,
            Action<PhemeDbContextOptions> configure)
        {
            var services = new ServiceCollection();
            services.AddSingleton<Probe>();
            services.AddPheme(options => options.AddHandlersFromAssemblyContaining<TestShop>());
            services.AddPhemeDbContext<ShopDbContext>(configure);

            void Sqlite(DbContextOptionsBuilder options) => options.UseSqlite(connection);
            _ = registration switch
            {
                "AddDbContext" => services.AddDbContext<ShopDbContext>(Sqlite),
                "AddDbContextFactory" => services.AddDbContextFactory<ShopDbContext>(Sqlite),
                "AddDbContextPool" => services.AddDbContextPool<ShopDbContext>(Sqlite),
                "AddPooledDbContextFactory" => services.AddPooledDbContextFactory<ShopDbContext>(Sqlite),
                "AddDbContext with singleton options" => services.AddDbContext<ShopDbContext>(Sqlite, optionsLifetime: ServiceLifetime.Singleton),
                "AddDbContext with transient options" => services.AddDbContext<ShopDbContext>(Sqlite, optionsLifetime: ServiceLifetime.Transient),
                "AddDbContextFactory, then AddDbContext" => services.AddDbContextFactory<ShopDbContext>(Sqlite).AddDbContext<ShopDbContext>(Sqlite),
                "AddDbContext, then AddDbContextFactory" => services.AddDbContext<ShopDbContext>(Sqlite).AddDbContextFactory<ShopDbContext>(Sqlite),
                "AddDbContextFactory with a scoped lifetime" => services.AddDbContextFactory<ShopDbContext>(Sqlite, ServiceLifetime.Scoped),
                "AddDbContext, then AddDbContextFactory with a scoped lifetime" =>
                    services.AddDbContext<ShopDbContext>(Sqlite).AddDbContextFactory<ShopDbContext>(Sqlite, ServiceLifetime.Scoped),
                _ => throw new ArgumentOutOfRangeException(nameof(registration), registration, null),
            };

            // As in Development: the container's own checks run too, and must not fail before Pheme's.
            return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        }
    }
}
