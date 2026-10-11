using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Npgsql;
using Ordering.Application.Orders.Confirmation;
using Shipping.Application.PrepareShipment;
using Testcontainers.PostgreSql;

namespace Pheme.Examples.IntegrationTests
{
    /// <summary>
    /// One PostgreSQL container and one shop application for all tests of <see cref="ShopCollection"/>. The tests of a
    /// collection run one at a time, so a test may clear <see cref="RequestLogs"/> before it starts. The application
    /// delivers its outbox only when a test calls <c>DrainOutbox</c>, and talks to fake services outside it.
    /// </summary>
    public sealed class ShopFixture : IAsyncLifetime
    {
        private readonly PostgreSqlContainer database = new PostgreSqlBuilder("postgres:17-alpine").Build();
        private WebApplicationFactory<Program>? factory;

        // Npgsql negotiates GSS encryption whenever the machine holds a Kerberos ticket; with the KDC unreachable (for
        // example outside a VPN) each attempt hangs ~40 s and the first start of the application times out.
        public string ConnectionString => new NpgsqlConnectionStringBuilder(database.GetConnectionString())
        {
            GssEncryptionMode = GssEncryptionMode.Disable,
        }.ConnectionString;

        public WebApplicationFactory<Program> Factory =>
            factory ?? throw new InvalidOperationException("The fixture is not initialized.");

        // Only Pheme's request logs, at the levels the application's configuration lets through.
        public FakeLogCollector RequestLogs => Factory.Services.GetFakeLogCollector();

        public FakeCustomerNotifier Notifier { get; } = new();

        public FakeCarrier Carrier { get; } = new();

        public async ValueTask InitializeAsync()
        {
            await database.StartAsync();
            factory = CreateFactoryWithOutboxProcessor().WithWebHostBuilder(TurnOutboxProcessorOff);
        }

        /// <summary>
        /// Creates another instance of the application on the same database and fakes, with the outbox processor
        /// polling every 100 ms in the background, as in production. The caller disposes it.
        /// </summary>
        public WebApplicationFactory<Program> CreateFactoryWithOutboxProcessor() => CreateFactory(ConnectionString);

        /// <summary>
        /// Creates another instance of the application, with the outbox processor turned off, on a new database of its
        /// own, so its outbox holds only the messages of the test: no message of another test is delivered or counted
        /// with them. The caller disposes it.
        /// </summary>
        public WebApplicationFactory<Program> CreateFactoryWithOwnDatabase() =>
            CreateFactory(new NpgsqlConnectionStringBuilder(ConnectionString) { Database = $"shop_{Guid.NewGuid():N}" }.ConnectionString)
                .WithWebHostBuilder(TurnOutboxProcessorOff);

        /// <summary>
        /// Creates another instance of the application on a new database of its own, as
        /// <see cref="CreateFactoryWithOwnDatabase()"/>, whose next save of a <typeparamref name="TContext"/> runs the
        /// action of <paramref name="interceptor"/> first. The caller disposes it.
        /// </summary>
        public WebApplicationFactory<Program> CreateFactoryWithOwnDatabase<TContext>(BeforeNextSaveInterceptor<TContext> interceptor)
            where TContext : DbContext =>
            CreateFactoryWithOwnDatabase().WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
                services.ConfigureDbContext<TContext>(options => options.AddInterceptors(interceptor))));

        private WebApplicationFactory<Program> CreateFactory(string connectionString) =>
            new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
                .UseSetting("ConnectionStrings:Shop", connectionString)
                .UseSetting("Outbox:PollingInterval", "00:00:00.100")
                .ConfigureLogging(logging => logging.AddFakeLogging(options =>
                    options.FilteredCategories.Add(RequestLoggingOptions.CategoryName)))
                .ConfigureTestServices(services => services
                    .AddSingleton<ICustomerNotifier>(Notifier)
                    .AddSingleton<ICarrier>(Carrier)));

        // The messages are delivered only when a test calls DrainOutbox.
        private static void TurnOutboxProcessorOff(IWebHostBuilder builder) =>
            builder.ConfigureTestServices(services => services.UsePhemeOutboxForTests());

        public async ValueTask DisposeAsync()
        {
            if (factory is not null)
            {
                await factory.DisposeAsync();
            }

            await database.DisposeAsync();
        }
    }

    [CollectionDefinition]
    public sealed class ShopCollection : ICollectionFixture<ShopFixture>;
}
