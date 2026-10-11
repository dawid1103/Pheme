using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Shop.Common.Persistence
{
    /// <summary>
    /// Creates the tables of a module's <see cref="DbContext"/> without migrations - enough for an example.
    /// </summary>
    public static class DatabaseSchemaExtensions
    {
        /// <summary>
        /// Creates the database if it does not exist, then the tables of <paramref name="context"/> unless its default
        /// schema exists already.
        /// </summary>
        /// <remarks>
        /// <c>Database.EnsureCreated</c> does nothing once the database has any table, so with one schema per module in
        /// one database only the first module would get its tables.
        /// </remarks>
        public static async Task EnsureSchemaCreatedAsync(this DbContext context, CancellationToken cancellationToken = default)
        {
            string schema = context.Model.GetDefaultSchema()
                ?? throw new InvalidOperationException($"{context.GetType().Name} has no default schema.");
            IRelationalDatabaseCreator creator = context.GetService<IRelationalDatabaseCreator>();
            if (!await creator.ExistsAsync(cancellationToken))
            {
                await creator.CreateAsync(cancellationToken);
            }

            bool schemaExists = await context.Database
                .SqlQuery<bool>($"""SELECT EXISTS (SELECT 1 FROM information_schema.schemata WHERE schema_name = {schema}) AS "Value" """)
                .SingleAsync(cancellationToken);
            if (!schemaExists)
            {
                await creator.CreateTablesAsync(cancellationToken);
            }
        }
    }
}
