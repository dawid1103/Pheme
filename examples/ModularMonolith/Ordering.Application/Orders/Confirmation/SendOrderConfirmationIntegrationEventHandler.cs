using Microsoft.EntityFrameworkCore;
using Ordering.Domain.Orders;
using Pheme;
using Shop.Common.Contracts;

namespace Ordering.Application.Orders.Confirmation
{
    // A handler of the module's own integration event: it runs once the order is saved, unlike the domain event
    // handlers, so a customer is never told of an order that was rolled back.
    internal sealed class SendOrderConfirmationIntegrationEventHandler(IOrderingDbContext db, ICustomerNotifier notifier)
        : IIntegrationEventHandler<OrderPlacedIntegrationEvent>
    {
        public async Task Handle(OrderPlacedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
        {
            // The event tells what was placed, the order what still holds: an order cancelled before the delivery is not
            // confirmed.
            OrderStatus status = await db.Orders
                .Where(o => o.Id == integrationEvent.OrderId)
                .Select(o => o.Status)
                .SingleAsync(cancellationToken);
            if (status == OrderStatus.Cancelled)
            {
                return;
            }

            await notifier.SendOrderConfirmation(
                integrationEvent.OrderId, integrationEvent.Product, integrationEvent.Quantity, cancellationToken);
        }
    }
}
