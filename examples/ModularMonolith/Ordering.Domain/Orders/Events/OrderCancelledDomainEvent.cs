namespace Ordering.Domain.Orders.Events
{
    /// <summary>
    /// Raised when a placed <see cref="Order"/> is cancelled.
    /// </summary>
    public sealed record OrderCancelledDomainEvent(Guid OrderId) : IDomainEvent;
}
