using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Pheme.EntityFrameworkCore.IntegrationTests
{
    /// <summary>
    /// One PostgreSQL container for the tests of <see cref="PostgresCollection"/>, each test with a database of its own.
    /// </summary>
    public sealed class PostgresDatabase : ITestDatabase, IAsyncLifetime
    {
        private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:17-alpine").Build();

        public Action<DbContextOptionsBuilder> NewDatabase()
        {
            // Npgsql negotiates GSS encryption whenever the machine holds a Kerberos ticket; with the KDC unreachable (for
            // example outside a VPN) each attempt hangs ~40 s.
            string connectionString = new NpgsqlConnectionStringBuilder(container.GetConnectionString())
            {
                Database = $"test_{Guid.NewGuid():N}",
                GssEncryptionMode = GssEncryptionMode.Disable,
            }.ConnectionString;
            return options => options.UseNpgsql(connectionString);
        }

        public async ValueTask InitializeAsync()
        {
            await container.StartAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await container.DisposeAsync();
        }
    }

    [CollectionDefinition]
    public sealed class PostgresCollection : ICollectionFixture<PostgresDatabase>;

    // The outbox tests on PostgreSQL; another supported database gets the same two classes.
    [Collection(typeof(PostgresCollection))]
    public sealed class PostgresOutboxWriterTests(PostgresDatabase database) : OutboxWriterTests(database);

    [Collection(typeof(PostgresCollection))]
    public sealed class PostgresOutboxStoreTests(PostgresDatabase database) : OutboxStoreTests(database);

    [Collection(typeof(PostgresCollection))]
    public sealed class PostgresOutboxTestingTests(PostgresDatabase database) : OutboxTestingTests(database);
}
