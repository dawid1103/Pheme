using Ordering.Domain.Orders.Events;
using Pheme;

namespace Ordering.Application.Orders.History
{
    // One class handling several events: registered by hand for OrderPlacedDomainEvent, where the order of the handlers
    // matters, and found by the scan for the others.
    internal sealed class RecordOrderHistoryDomainEventHandler(IOrderingDbContext db)
        : IDomainEventHandler<OrderPlacedDomainEvent>,
          IDomainEventHandler<OrderCancelledDomainEvent>,
          IDomainEventHandler<OrderShippedDomainEvent>
    {
        public Task Handle(OrderPlacedDomainEvent domainEvent, CancellationToken cancellationToken) =>
            db.Record(domainEvent.OrderId, $"Placed {domainEvent.Quantity} x {domainEvent.Product}", cancellationToken);

        public Task Handle(OrderCancelledDomainEvent domainEvent, CancellationToken cancellationToken) =>
            db.Record(domainEvent.OrderId, "Cancelled", cancellationToken);

        public Task Handle(OrderShippedDomainEvent domainEvent, CancellationToken cancellationToken) =>
            db.Record(domainEvent.OrderId, "Shipped", cancellationToken);
    }
}
