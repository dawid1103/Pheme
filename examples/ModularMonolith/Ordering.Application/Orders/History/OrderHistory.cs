using Microsoft.EntityFrameworkCore;
using Ordering.Domain.Orders;

namespace Ordering.Application.Orders.History
{
    internal static class OrderHistory
    {
        /// <summary>
        /// Adds the next entry of an order's history, saved with the rest of the changes.
        /// </summary>
        public static async Task Record(this IOrderingDbContext db, Guid orderId, string description, CancellationToken cancellationToken)
        {
            // Domain event handlers run before the changes are saved, so the entries recorded by the handlers before
            // this one are not in the database yet.
            int lastSaved = await db.OrderHistory
                .Where(entry => entry.OrderId == orderId)
                .MaxAsync(entry => (int?)entry.Sequence, cancellationToken) ?? 0;
            int lastPending = db.OrderHistory.Local
                .Where(entry => entry.OrderId == orderId)
                .Max(entry => (int?)entry.Sequence) ?? 0;

            db.OrderHistory.Add(OrderHistoryEntry.Record(orderId, Math.Max(lastSaved, lastPending) + 1, description));
        }
    }
}
