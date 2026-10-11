using Pheme;

namespace Ordering.Application.Orders.GetOrder
{
    /// <summary>
    /// Reads an order; throws <see cref="Shop.Common.Errors.NotFoundException"/> when it does not exist.
    /// </summary>
    public sealed record GetOrderQuery(Guid OrderId) : IQuery<OrderDto>;
}
