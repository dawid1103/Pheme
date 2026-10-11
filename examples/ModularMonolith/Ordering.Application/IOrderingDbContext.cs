using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Ordering.Application.Behaviors;
using Ordering.Domain.Inventory;
using Ordering.Domain.Orders;

namespace Ordering.Application
{
    /// <summary>
    /// The ordering data the handlers work with, implemented by the infrastructure.
    /// </summary>
    public interface IOrderingDbContext
    {
        /// <summary>
        /// The orders.
        /// </summary>
        DbSet<Order> Orders { get; }

        /// <summary>
        /// The stocks of the counted products.
        /// </summary>
        DbSet<ProductStock> ProductStocks { get; }

        /// <summary>
        /// The requests for more items of the products running low.
        /// </summary>
        DbSet<RestockRequest> RestockRequests { get; }

        /// <summary>
        /// The history entries of the orders.
        /// </summary>
        DbSet<OrderHistoryEntry> OrderHistory { get; }

        /// <summary>
        /// The audit trail of the commands that changed the module's data (<c>AuditBehavior</c>).
        /// </summary>
        DbSet<AuditEntry> AuditTrail { get; }

        /// <summary>
        /// The commands handled with an idempotency key, and their responses (<c>IdempotencyBehavior</c>).
        /// </summary>
        DbSet<ProcessedCommand> ProcessedCommands { get; }

        /// <summary>
        /// The database of the context, for a transaction around a whole command (<c>TransactionBehavior</c>).
        /// </summary>
        DatabaseFacade Database { get; }

        /// <summary>
        /// Publishes the domain events of the changed entities and saves the changes, in one transaction.
        /// </summary>
        Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    }
}
