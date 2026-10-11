using System.Collections.Concurrent;
using Ordering.Application.Orders.Confirmation;

namespace Pheme.Examples.IntegrationTests
{
    /// <summary>
    /// Records the confirmations instead of sending them.
    /// </summary>
    public sealed class FakeCustomerNotifier : ICustomerNotifier
    {
        private readonly ConcurrentQueue<Guid> confirmedOrders = new();

        /// <summary>
        /// How many times the order was confirmed to the customer.
        /// </summary>
        public int ConfirmationsOf(Guid orderId) => confirmedOrders.Count(id => id == orderId);

        public Task SendOrderConfirmation(Guid orderId, string product, int quantity, CancellationToken cancellationToken)
        {
            confirmedOrders.Enqueue(orderId);
            return Task.CompletedTask;
        }
    }
}
