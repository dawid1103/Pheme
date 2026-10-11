using Microsoft.EntityFrameworkCore;

namespace Pheme.EntityFrameworkCore.TestFixtures
{
    /// <summary>
    /// Another module's context, with its outbox table in a schema of its own.
    /// </summary>
    public sealed class ShippingDbContext(DbContextOptions<ShippingDbContext> options) : DbContext(options)
    {
        public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultSchema("shipping");
            modelBuilder.AddPhemeOutbox("messages", "shipping_outbox");
        }
    }
}
