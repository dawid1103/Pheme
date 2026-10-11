using Microsoft.EntityFrameworkCore;
using Pheme;
using Shipping.Domain;
using Shipping.Infrastructure;
using Shop.Common.Contracts;

namespace Shipping.Application.CancelShipment
{
    internal sealed class CancelShipmentIntegrationEventHandler(ShippingDbContext db)
        : IIntegrationEventHandler<OrderCancelledIntegrationEvent>
    {
        public async Task Handle(OrderCancelledIntegrationEvent integrationEvent, CancellationToken cancellationToken)
        {
            Shipment? shipment = await db.Shipments.SingleOrDefaultAsync(s => s.OrderId == integrationEvent.OrderId, cancellationToken);
            if (shipment is null)
            {
                // OrderPlacedIntegrationEvent is not delivered yet, as its delivery failed so far: the cancelled shipment
                // keeps PrepareShipmentIntegrationEventHandler, idempotent by the order, from preparing it when it is.
                db.Shipments.Add(Shipment.CancelBeforePreparation(integrationEvent.OrderId));
            }
            else
            {
                // Delivered at least once: a cancelled shipment, or one whose return is requested, changes nothing.
                shipment.Cancel();
            }

            // A dispatch saved meanwhile fails this save on the shipment's row version; the outbox tries again, and the
            // next attempt sees the dispatched shipment.
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
