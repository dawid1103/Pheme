namespace Ordering.Domain.Orders.Events
{
    /// <summary>
    /// Raised when the shipping module has dispatched a placed <see cref="Order"/>.
    /// </summary>
    public sealed record OrderShippedDomainEvent(Guid OrderId) : IDomainEvent;
}
