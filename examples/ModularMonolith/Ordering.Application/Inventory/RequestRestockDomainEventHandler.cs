using Ordering.Domain.Inventory;
using Ordering.Domain.Inventory.Events;
using Pheme;

namespace Ordering.Application.Inventory
{
    // Handles an event raised by another handler (AllocateStockDomainEventHandler): published in the next round of the
    // same save, so the request is saved with the order or not at all.
    internal sealed class RequestRestockDomainEventHandler(IOrderingDbContext db)
        : IDomainEventHandler<ReorderLevelReachedDomainEvent>
    {
        public Task Handle(ReorderLevelReachedDomainEvent domainEvent, CancellationToken cancellationToken)
        {
            db.RestockRequests.Add(RestockRequest.Create(domainEvent.Product, domainEvent.Available));
            return Task.CompletedTask;
        }
    }
}
