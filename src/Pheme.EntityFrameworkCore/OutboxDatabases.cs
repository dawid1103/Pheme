using Microsoft.EntityFrameworkCore;

namespace Pheme
{
    /// <summary>
    /// The databases the outbox supports, by the EF Core provider of the context: the ones its tests run on.
    /// </summary>
    /// <remarks>
    /// A database is added with an entry here once the outbox tests pass on it (<c>docs/development.md</c>, "Adding a
    /// database"). The provider is read from the context, not configured, so it cannot disagree with it.
    /// </remarks>
    internal static class OutboxDatabases
    {
        // EF Core provider name → database name.
        private static readonly Dictionary<string, string> Supported = new(StringComparer.Ordinal)
        {
            ["Npgsql.EntityFrameworkCore.PostgreSQL"] = "PostgreSQL",
        };

        /// <exception cref="InvalidOperationException">The provider of <paramref name="context"/> is not supported.</exception>
        public static void EnsureSupported(DbContext context, Type contextType)
        {
            if (context.Database.ProviderName is { } provider && Supported.ContainsKey(provider))
            {
                return;
            }

            throw new InvalidOperationException(
                $"{contextType.Name} writes integration events to its outbox on the database provider "
                + $"{context.Database.ProviderName ?? "(none)"}, but the outbox of Pheme supports only "
                + $"{string.Join(", ", Supported.Select(database => $"{database.Value} ({database.Key})"))}, "
                + "the databases its tests run on.");
        }
    }
}
