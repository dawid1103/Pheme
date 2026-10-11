using Microsoft.EntityFrameworkCore;
using Ordering.Domain.Orders;
using Pheme;
using Shop.Common.Errors;

namespace Ordering.Application.Orders.GetOrder
{
    internal sealed class GetOrderQueryHandler(IOrderingDbContext db) : IQueryHandler<GetOrderQuery, OrderDto>
    {
        public async Task<OrderDto> Handle(GetOrderQuery query, CancellationToken cancellationToken)
        {
            Order order = await db.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == query.OrderId, cancellationToken)
                ?? throw new NotFoundException($"Order {query.OrderId} does not exist.");

            return new OrderDto(order.Id, order.Product, order.Quantity, order.Status.ToString());
        }
    }
}
