using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Pheme.EntityFrameworkCore.UnitTests
{
    /// <summary>
    /// An application with Pheme, domain event dispatch and a <see cref="ShopDbContext"/> on an in-memory SQLite
    /// database kept for the lifetime of the test.
    /// </summary>
    internal sealed class TestShop : IDisposable
    {
        private readonly SqliteConnection connection;
        private readonly ServiceProvider provider;

        private TestShop(Action<IServiceCollection> addPhemeDbContext, bool addAfterDbContext)
        {
            connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();

            var services = new ServiceCollection();
            services.AddSingleton<Probe>();
            services.AddPheme(options => options.AddHandlersFromAssemblyContaining<TestShop>());
            if (!addAfterDbContext)
            {
                addPhemeDbContext(services);
            }

            services.AddDbContext<ShopDbContext>(options => options.UseSqlite(connection));
            if (addAfterDbContext)
            {
                addPhemeDbContext(services);
            }

            provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

            using IServiceScope scope = provider.CreateScope();
            Db(scope).Database.EnsureCreated();
        }

        public Probe Probe => provider.GetRequiredService<Probe>();

        public SqliteConnection Connection => connection;

        /// <summary>
        /// Collects from <see cref="IHasDomainEvents"/> and <see cref="AggregateRoot"/>, then applies <paramref name="configure"/>.
        /// </summary>
        public static TestShop Create(Action<PhemeDbContextOptions>? configure = null)
        {
            return new TestShop(
                services => services.AddPhemeDbContext<ShopDbContext>(options =>
                {
                    CollectFromInterface(options);
                    CollectFromBaseClass(options);
                    configure?.Invoke(options);
                }),
                addAfterDbContext: false);
        }

        public static TestShop CreateWith(Action<IServiceCollection> addPhemeDbContext, bool addAfterDbContext = false)
        {
            return new TestShop(addPhemeDbContext, addAfterDbContext);
        }

        public static void CollectFromInterface(PhemeDbContextOptions options)
        {
            options.CollectDomainEventsFrom<IHasDomainEvents>(entity => entity.DomainEvents, entity => entity.ClearDomainEvents());
        }

        public static void CollectFromBaseClass(PhemeDbContextOptions options)
        {
            options.CollectDomainEventsFrom<AggregateRoot>(entity => entity.Events, entity => entity.ClearEvents());
        }

        public static ShopDbContext Db(IServiceScope scope)
        {
            return scope.ServiceProvider.GetRequiredService<ShopDbContext>();
        }

        public IServiceScope CreateScope()
        {
            return provider.CreateScope();
        }

        public async Task<string[]> SavedOrders()
        {
            using IServiceScope scope = provider.CreateScope();
            return await Db(scope).Orders
                .OrderBy(order => order.Id)
                .Select(order => order.Name)
                .ToArrayAsync(TestContext.Current.CancellationToken);
        }

        public async Task<string[]> SavedNotes()
        {
            using IServiceScope scope = provider.CreateScope();
            return await Db(scope).Notes
                .OrderBy(note => note.Id)
                .Select(note => note.Text)
                .ToArrayAsync(TestContext.Current.CancellationToken);
        }

        public void Dispose()
        {
            provider.Dispose();
            connection.Dispose();
        }
    }
}
