using Microsoft.EntityFrameworkCore;
using Ordering.Application;
using Ordering.Application.Behaviors;
using Ordering.Domain.Inventory;
using Ordering.Domain.Orders;

namespace Ordering.Infrastructure
{
    /// <summary>
    /// The ordering module's tables, in the <c>ordering</c> schema, with its outbox. Its domain events are published, and
    /// its outbox is written, as set up in <see cref="OrderingServiceCollectionExtensions.AddOrdering"/>.
    /// </summary>
    public sealed class OrderingDbContext(DbContextOptions<OrderingDbContext> options) : DbContext(options), IOrderingDbContext
    {
        /// <inheritdoc />
        public DbSet<Order> Orders => Set<Order>();

        /// <inheritdoc />
        public DbSet<ProductStock> ProductStocks => Set<ProductStock>();

        /// <inheritdoc />
        public DbSet<RestockRequest> RestockRequests => Set<RestockRequest>();

        /// <inheritdoc />
        public DbSet<OrderHistoryEntry> OrderHistory => Set<OrderHistoryEntry>();

        /// <inheritdoc />
        public DbSet<AuditEntry> AuditTrail => Set<AuditEntry>();

        /// <inheritdoc />
        public DbSet<ProcessedCommand> ProcessedCommands => Set<ProcessedCommand>();

        /// <inheritdoc />
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultSchema("ordering");

            // The table outbox_messages of Pheme, in the ordering schema; created with the other tables.
            modelBuilder.AddPhemeOutbox();

            modelBuilder.Entity<Order>(order =>
            {
                order.Property(o => o.Product).HasMaxLength(200);
                order.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);
                order.Ignore(o => o.DomainEvents);

                // A cancellation and the delivery of the shipment's dispatch changing one order at once: one of them
                // fails instead of overwriting the other. Npgsql maps a uint row version to PostgreSQL's xmin system
                // column, which every update changes, so the table needs no column of its own.
                order.Property<uint>("RowVersion").IsRowVersion();
            });

            modelBuilder.Entity<ProductStock>(stock =>
            {
                stock.HasKey(s => s.Product);
                stock.Property(s => s.Product).HasMaxLength(200);
                stock.Ignore(s => s.DomainEvents);

                // Two orders allocating the same stock at once: one of them fails instead of selling more than there is.
                stock.Property<uint>("RowVersion").IsRowVersion();
            });

            modelBuilder.Entity<RestockRequest>(request => request.Property(r => r.Product).HasMaxLength(200));

            modelBuilder.Entity<OrderHistoryEntry>(entry =>
            {
                entry.Property(e => e.Description).HasMaxLength(500);

                // Two requests numbering the history of one order at once: one of them fails instead of a duplicate.
                entry.HasIndex(e => new { e.OrderId, e.Sequence }).IsUnique();
            });

            modelBuilder.Entity<AuditEntry>(entry =>
            {
                entry.Property(e => e.User).HasMaxLength(200);
                entry.Property(e => e.Command).HasMaxLength(200);
            });

            // The idempotency key is the primary key: of two requests with one key at once, the later save fails.
            modelBuilder.Entity<ProcessedCommand>(command => command.HasKey(c => c.IdempotencyKey));
        }
    }
}
