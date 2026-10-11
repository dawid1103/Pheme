using Ordering.Application.Behaviors;
using Pheme;

namespace Ordering.Application.Orders.CancelOrder
{
    /// <summary>
    /// Cancels an order that is not shipped; the shipping module learns of it through the outbox. Audited.
    /// </summary>
    public sealed record CancelOrderCommand(Guid OrderId) : ICommand, IAuditedCommand;
}
