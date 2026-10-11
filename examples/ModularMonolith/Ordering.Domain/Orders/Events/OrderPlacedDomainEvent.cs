namespace Ordering.Domain.Orders.Events
{
    /// <summary>
    /// Raised when an <see cref="Order"/> is placed.
    /// </summary>
    public sealed record OrderPlacedDomainEvent(Guid OrderId, string Product, int Quantity, bool Express) : IDomainEvent;
}
