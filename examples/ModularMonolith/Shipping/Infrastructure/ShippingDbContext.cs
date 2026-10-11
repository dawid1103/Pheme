using Microsoft.EntityFrameworkCore;
using Shipping.Domain;

namespace Shipping.Infrastructure
{
    /// <summary>
    /// The shipping module's tables, in the <c>shipping</c> schema, with its outbox. The module's handlers use it directly,
    /// without an interface of their own.
    /// </summary>
    public sealed class ShippingDbContext(DbContextOptions<ShippingDbContext> options) : DbContext(options)
    {
        /// <summary>
        /// The shipments.
        /// </summary>
        public DbSet<Shipment> Shipments => Set<Shipment>();

        /// <summary>
        /// The instructions for the warehouse staff.
        /// </summary>
        public DbSet<WarehouseNotice> WarehouseNotices => Set<WarehouseNotice>();

        /// <inheritdoc />
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultSchema("shipping");

            // The table outbox_messages of Pheme, in the shipping schema: the module's own outbox.
            modelBuilder.AddPhemeOutbox();

            modelBuilder.Entity<Shipment>(shipment =>
            {
                shipment.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);
                shipment.HasIndex(s => s.OrderId).IsUnique();
                shipment.Ignore(s => s.DomainEvents);

                // A dispatch and the delivery of the order's cancellation changing one shipment at once: one of them
                // fails instead of overwriting the other (PostgreSQL's xmin, as in the ordering module).
                shipment.Property<uint>("RowVersion").IsRowVersion();
            });

            modelBuilder.Entity<WarehouseNotice>(notice =>
            {
                notice.Property(n => n.Instruction).HasMaxLength(200);
                notice.HasIndex(n => n.ShipmentId);
            });
        }
    }
}
