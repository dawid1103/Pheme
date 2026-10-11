using Microsoft.EntityFrameworkCore;
using Ordering.Domain.Orders;
using Pheme;
using Shop.Common.Contracts;

namespace Ordering.Application.Orders.MarkShipped
{
    // A handler of another module's integration event changing this module: it saves its own DbContext, whose domain
    // events (OrderShippedDomainEvent, recorded in the history) are published by that save.
    internal sealed class MarkOrderShippedIntegrationEventHandler(IOrderingDbContext db)
        : IIntegrationEventHandler<ShipmentDispatchedIntegrationEvent>
    {
        public async Task Handle(ShipmentDispatchedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
        {
            Order order = await db.Orders.SingleAsync(o => o.Id == integrationEvent.OrderId, cancellationToken);

            // Delivered at least once: a shipped order is not shipped again. A cancelled one stays cancelled; the
            // shipping module, told of the cancellation, asks for the parcel back.
            order.MarkShipped();
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
