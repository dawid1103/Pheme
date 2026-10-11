using Microsoft.EntityFrameworkCore;
using Ordering.Application.Orders.History;
using Ordering.Domain.Inventory;
using Ordering.Domain.Orders.Events;
using Pheme;
using Shop.Common.Errors;

namespace Ordering.Application.Inventory
{
    internal sealed class AllocateStockDomainEventHandler(IOrderingDbContext db)
        : IDomainEventHandler<OrderPlacedDomainEvent>
    {
        public async Task Handle(OrderPlacedDomainEvent domainEvent, CancellationToken cancellationToken)
        {
            ProductStock? stock = await db.ProductStocks.SingleOrDefaultAsync(s => s.Product == domainEvent.Product, cancellationToken);
            if (stock is null)
            {
                return;
            }

            // Nothing is saved yet: the order and the history recorded by the handlers before this one are discarded too.
            if (!stock.CanAllocate(domainEvent.Quantity))
            {
                throw new BusinessRuleException(
                    $"Only {stock.Available} items of {stock.Product} are available, not {domainEvent.Quantity}.");
            }

            // May raise ReorderLevelReachedDomainEvent, published in a second round before the same save.
            stock.Allocate(domainEvent.Quantity);
            await db.Record(domainEvent.OrderId, $"Allocated {domainEvent.Quantity} x {domainEvent.Product}", cancellationToken);
        }
    }
}
