using Pheme;
using Shipping.Domain.Events;
using Shop.Common.Contracts;

namespace Shipping.Application.DispatchShipment
{
    internal sealed class WriteShipmentDispatchedToOutboxDomainEventHandler(
        IOutboxWriter<ShippingModule> outbox,
        TimeProvider timeProvider)
        : IDomainEventHandler<ShipmentDispatchedDomainEvent>
    {
        public Task Handle(ShipmentDispatchedDomainEvent domainEvent, CancellationToken cancellationToken)
        {
            // Saved with the dispatched shipment, in the shipping module's own outbox.
            outbox.Add(new ShipmentDispatchedIntegrationEvent(Guid.CreateVersion7(), timeProvider.GetUtcNow(), domainEvent.OrderId));
            return Task.CompletedTask;
        }
    }
}
