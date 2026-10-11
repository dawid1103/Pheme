using Microsoft.EntityFrameworkCore;

namespace Pheme.EntityFrameworkCore.UnitTests
{
    public sealed class ShopDbContext(DbContextOptions<ShopDbContext> options) : DbContext(options)
    {
        public DbSet<Order> Orders => Set<Order>();

        public DbSet<Note> Notes => Set<Note>();

        public DbSet<Shipment> Shipments => Set<Shipment>();

        public DbSet<Hybrid> Hybrids => Set<Hybrid>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Order>().Ignore(order => order.DomainEvents);
            modelBuilder.Entity<Shipment>().Ignore(shipment => shipment.Events);
            modelBuilder.Entity<Hybrid>().Ignore(hybrid => hybrid.Events).Ignore(hybrid => hybrid.DomainEvents);
        }
    }
}
