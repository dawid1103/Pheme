using Microsoft.EntityFrameworkCore;
using Pheme;
using Shipping.Domain;
using Shipping.Infrastructure;
using Shop.Common.Contracts;

namespace Shipping.Application.PrepareShipment
{
    internal sealed class PrepareShipmentIntegrationEventHandler(ShippingDbContext db, ICarrier carrier)
        : IIntegrationEventHandler<OrderPlacedIntegrationEvent>
    {
        public async Task Handle(OrderPlacedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
        {
            // Delivered at least once: an order whose shipment exists already is not prepared again, and neither is an
            // order cancelled before the delivery, whose shipment is saved cancelled.
            if (await db.Shipments.AnyAsync(s => s.OrderId == integrationEvent.OrderId, cancellationToken))
            {
                return;
            }

            db.Shipments.Add(Shipment.Prepare(integrationEvent.OrderId, integrationEvent.Express));

            // A failing carrier fails the delivery before anything is saved; the outbox tries the message again later.
            await carrier.SchedulePickup(integrationEvent.OrderId, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
