using Microsoft.EntityFrameworkCore;
using Pheme;
using Shipping.Domain;
using Shipping.Infrastructure;
using Shop.Common.Errors;

namespace Shipping.Application.DispatchShipment
{
    internal sealed class DispatchShipmentCommandHandler(ShippingDbContext db) : ICommandHandler<DispatchShipmentCommand>
    {
        public async Task Handle(DispatchShipmentCommand command, CancellationToken cancellationToken)
        {
            Shipment shipment = await db.Shipments.SingleOrDefaultAsync(s => s.OrderId == command.OrderId, cancellationToken)
                ?? throw new NotFoundException($"Order {command.OrderId} has no shipment.");

            if (!shipment.CanDispatch)
            {
                throw new BusinessRuleException($"The shipment of order {command.OrderId} is cancelled and cannot be dispatched.");
            }

            // ShipmentDispatchedDomainEvent writes ShipmentDispatchedIntegrationEvent for the ordering module, saved with
            // the shipment.
            shipment.Dispatch();
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
