using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Pheme.EntityFrameworkCore.IntegrationTests
{
    /// <summary>
    /// An application with the outbox of Pheme, a handler doing nothing for each integration event of the tests, and the
    /// <see cref="OrderingDbContext"/> and <see cref="ShippingDbContext"/> of two modules in one new database, each with
    /// its tables created.
    /// </summary>
    internal sealed class OutboxApp : IAsyncDisposable
    {
        private readonly ServiceProvider provider;

        private OutboxApp(ServiceProvider provider)
        {
            this.provider = provider;
        }

        /// <param name="database">The container to create the database in.</param>
        /// <param name="addPhemeDbContexts">Configures what Pheme does with the contexts.</param>
        public static async Task<OutboxApp> Create(ITestDatabase database, Action<IServiceCollection> addPhemeDbContexts)
        {
            Action<DbContextOptionsBuilder> useDatabase = database.NewDatabase();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddPheme(options => options.AddOutbox());

            // The outbox accepts only events with a handler; the tests add their own after these.
            services.AddTransient<IIntegrationEventHandler<OrderSubmittedIntegrationEvent>, IgnoringIntegrationEventHandler<OrderSubmittedIntegrationEvent>>();
            services.AddTransient<IIntegrationEventHandler<ExpressOrderSubmittedIntegrationEvent>, IgnoringIntegrationEventHandler<ExpressOrderSubmittedIntegrationEvent>>();
            services.AddTransient<IIntegrationEventHandler<ParcelSentIntegrationEvent>, IgnoringIntegrationEventHandler<ParcelSentIntegrationEvent>>();
            services.AddDbContext<OrderingDbContext>(useDatabase);
            services.AddDbContext<ShippingDbContext>(useDatabase);
            addPhemeDbContexts(services);
            var app = new OutboxApp(services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }));

            using IServiceScope scope = app.CreateScope();
            await CreateTables(scope.ServiceProvider.GetRequiredService<OrderingDbContext>());
            await CreateTables(scope.ServiceProvider.GetRequiredService<ShippingDbContext>());
            return app;
        }

        public IServiceProvider Services => provider;

        public IServiceScope CreateScope()
        {
            return provider.CreateScope();
        }

        /// <summary>
        /// Reads the saved outbox messages of <typeparamref name="TContext"/> in a new scope, in the order they occurred.
        /// </summary>
        public async Task<OutboxMessage[]> SavedMessages<TContext>()
            where TContext : DbContext
        {
            using IServiceScope scope = provider.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<TContext>().Set<OutboxMessage>()
                .AsNoTracking()
                .OrderBy(message => message.OccurredOnUtc)
                .ToArrayAsync(TestContext.Current.CancellationToken);
        }

        public async Task<string[]> SavedNotes()
        {
            using IServiceScope scope = provider.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<OrderingDbContext>().Notes
                .OrderBy(note => note.Id)
                .Select(note => note.Text)
                .ToArrayAsync(TestContext.Current.CancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            await provider.DisposeAsync();
        }

        /// <summary>
        /// Creates the database if needed and the tables of <paramref name="context"/>, so both contexts share it;
        /// <c>EnsureCreated</c> does nothing once the database has any table.
        /// </summary>
        private static async Task CreateTables(DbContext context)
        {
            var creator = context.GetService<IRelationalDatabaseCreator>();
            if (!await creator.ExistsAsync(TestContext.Current.CancellationToken))
            {
                await creator.CreateAsync(TestContext.Current.CancellationToken);
            }

            await creator.CreateTablesAsync(TestContext.Current.CancellationToken);
        }
    }
}
