using Microsoft.EntityFrameworkCore;

namespace Pheme.EntityFrameworkCore.UnitTests
{
    internal sealed class ShopDomainEventHandler(ShopDbContext db, Probe probe)
        : IDomainEventHandler<OrderPlacedDomainEvent>,
          IDomainEventHandler<OrderRejectedDomainEvent>,
          IDomainEventHandler<OrderConfirmedDomainEvent>,
          IDomainEventHandler<PingDomainEvent>,
          IDomainEventHandler<ShipmentDispatchedDomainEvent>
    {
        public async Task Handle(OrderPlacedDomainEvent domainEvent, CancellationToken cancellationToken)
        {
            int savedOrders = await db.Orders.CountAsync(cancellationToken);
            probe.Log.Add($"placed: {domainEvent.Name} ({savedOrders} orders saved)");
            probe.Contexts.Add(db);
            db.Notes.Add(new Note($"placed: {domainEvent.Name}"));
        }

        public Task Handle(OrderRejectedDomainEvent domainEvent, CancellationToken cancellationToken)
        {
            var exception = new RejectedException(domainEvent.Reason);
            probe.Thrown = exception;
            throw exception;
        }

        public Task Handle(OrderConfirmedDomainEvent domainEvent, CancellationToken cancellationToken)
        {
            probe.Log.Add($"confirmed: {domainEvent.Order.Name}");
            domainEvent.Order.Name += " (confirmed)";
            var followUp = new Order("follow-up");
            followUp.Raise(new OrderPlacedDomainEvent("follow-up"));
            db.Orders.Add(followUp);
            return Task.CompletedTask;
        }

        public Task Handle(PingDomainEvent domainEvent, CancellationToken cancellationToken)
        {
            probe.Log.Add("ping");
            domainEvent.Order.Raise(new PingDomainEvent(domainEvent.Order));
            return Task.CompletedTask;
        }

        public Task Handle(ShipmentDispatchedDomainEvent domainEvent, CancellationToken cancellationToken)
        {
            probe.Log.Add($"dispatched: {domainEvent.Name}");
            return Task.CompletedTask;
        }
    }
}
