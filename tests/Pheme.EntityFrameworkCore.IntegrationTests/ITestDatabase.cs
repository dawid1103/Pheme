using Microsoft.EntityFrameworkCore;

namespace Pheme.EntityFrameworkCore.IntegrationTests
{
    /// <summary>
    /// A database server the outbox tests run on; each supported database has one, with a collection of its own.
    /// </summary>
    public interface ITestDatabase
    {
        /// <summary>
        /// Gets the configuration of contexts using a new database, created by the first context that creates its tables
        /// and shared by all contexts configured with it.
        /// </summary>
        Action<DbContextOptionsBuilder> NewDatabase();
    }
}
