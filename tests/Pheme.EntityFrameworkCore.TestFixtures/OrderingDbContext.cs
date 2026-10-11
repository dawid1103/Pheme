using Microsoft.EntityFrameworkCore;

namespace Pheme.EntityFrameworkCore.TestFixtures
{
    /// <summary>
    /// A module's context with its own schema and the default outbox table.
    /// </summary>
    public sealed class OrderingDbContext(DbContextOptions<OrderingDbContext> options) : DbContext(options)
    {
        public DbSet<Note> Notes => Set<Note>();

        public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultSchema("ordering");
            modelBuilder.AddPhemeOutbox();
        }
    }
}
