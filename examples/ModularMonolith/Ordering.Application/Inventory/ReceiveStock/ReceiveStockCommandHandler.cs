using Microsoft.EntityFrameworkCore;
using Ordering.Domain.Inventory;
using Pheme;
using Shop.Common.Errors;

namespace Ordering.Application.Inventory.ReceiveStock
{
    internal sealed class ReceiveStockCommandHandler(IOrderingDbContext db) : ICommandHandler<ReceiveStockCommand>
    {
        public async Task Handle(ReceiveStockCommand command, CancellationToken cancellationToken)
        {
            ProductStock stock = await db.ProductStocks.SingleOrDefaultAsync(s => s.Product == command.Product, cancellationToken)
                ?? throw new NotFoundException($"Product {command.Product} has no stock.");

            // One statement for all the open requests, written at once rather than by SaveChangesAsync below. The
            // command's transaction (TransactionBehavior) rolls it back when the save fails, for example on a conflict.
            await db.RestockRequests.Where(r => r.Product == command.Product).ExecuteDeleteAsync(cancellationToken);

            stock.Receive(command.Quantity);
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
