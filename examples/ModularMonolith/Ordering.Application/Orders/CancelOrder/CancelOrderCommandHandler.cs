using Microsoft.EntityFrameworkCore;
using Ordering.Domain.Orders;
using Pheme;
using Shop.Common.Errors;

namespace Ordering.Application.Orders.CancelOrder
{
    internal sealed class CancelOrderCommandHandler(IOrderingDbContext db) : ICommandHandler<CancelOrderCommand>
    {
        public async Task Handle(CancelOrderCommand command, CancellationToken cancellationToken)
        {
            Order order = await db.Orders.SingleOrDefaultAsync(o => o.Id == command.OrderId, cancellationToken)
                ?? throw new NotFoundException($"Order {command.OrderId} does not exist.");

            // The module decides from its own data: an order is shipped once ShipmentDispatchedIntegrationEvent is
            // delivered. Until then the shipping module may be dispatching it, and handles the cancellation itself.
            if (!order.CanCancel)
            {
                throw new BusinessRuleException($"Order {order.Id} is shipped and can no longer be cancelled.");
            }

            // OrderCancelledDomainEvent writes OrderCancelledIntegrationEvent for the shipping module, saved with the order.
            order.Cancel();
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
