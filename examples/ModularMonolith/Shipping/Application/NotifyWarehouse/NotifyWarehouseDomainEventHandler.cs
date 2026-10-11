using Pheme;
using Shipping.Domain;
using Shipping.Domain.Events;
using Shipping.Infrastructure;

namespace Shipping.Application.NotifyWarehouse
{
    internal sealed class NotifyWarehouseDomainEventHandler(ShippingDbContext db)
        : IDomainEventHandler<ShipmentPreparationCancelledDomainEvent>,
          IDomainEventHandler<ShipmentReturnRequestedDomainEvent>
    {
        // Not saved here: the notice is saved with the shipment, by the SaveChangesAsync that published the event, in the
        // same transaction.
        public Task Handle(ShipmentPreparationCancelledDomainEvent domainEvent, CancellationToken cancellationToken) =>
            Notify(domainEvent.ShipmentId, $"Return the items of order {domainEvent.OrderId} to the shelves.");

        public Task Handle(ShipmentReturnRequestedDomainEvent domainEvent, CancellationToken cancellationToken) =>
            Notify(domainEvent.ShipmentId, $"Order {domainEvent.OrderId} was cancelled after dispatch: get the parcel back from the carrier.");

        private Task Notify(Guid shipmentId, string instruction)
        {
            db.WarehouseNotices.Add(WarehouseNotice.Create(shipmentId, instruction));
            return Task.CompletedTask;
        }
    }
}
