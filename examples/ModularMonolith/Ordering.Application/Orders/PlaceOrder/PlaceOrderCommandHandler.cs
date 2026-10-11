using Ordering.Domain.Orders;
using Pheme;

namespace Ordering.Application.Orders.PlaceOrder
{
    internal sealed class PlaceOrderCommandHandler(IOrderingDbContext db) : ICommandHandler<PlaceOrderCommand, Guid>
    {
        public async Task<Guid> Handle(PlaceOrderCommand command, CancellationToken cancellationToken)
        {
            Order order = Order.Place(command.Product, command.Quantity, command.Express);
            db.Orders.Add(order);
            await db.SaveChangesAsync(cancellationToken);
            return order.Id;
        }
    }
}
